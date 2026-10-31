"""
hs2n_bridge - client of the HS2Next automation bridge (HTTP/JSON on 127.0.0.1:17322).

One request is a JSON object {"cmd": "<group.verb>", ...}; the reply is {"ok": true, "cmd": ..., ...} or
{"ok": false, "error": "<text>", "code": "<stable code>", "detail": {...}} (HTTP 200 either way). `call` takes the same
request objects as the HS2-side Studio Bridge's clients, so a script can target HS2's Studio Bridge
(http://127.0.0.1:17321/) or HS2Next by URL alone.

Endpoint discovery (first match):
  1. url= argument / --url
  2. environment HS2NEXT_BRIDGE: a URL, an endpoint.json, its Bridge folder or the HS2Next UserData folder
  3. <UserData>/Bridge/endpoint.json of the UserData named by user_data= / --userdata / HS2NEXT_USERDATA (that folder only)
  4. otherwise the candidates <repo>/UserData (the Editor), <HS2NEXT_DATA_ROOT>/UserData and the per-user folder of the player
     (%USERPROFILE%\\AppData\\LocalLow\\stp3\\HS2Next\\UserData): among their endpoint.json files whose pid is a live process,
     the newest startedAt wins (the file of a process that ended is skipped)
  5. http://127.0.0.1:17322/
The token (only when the bridge requires one): token= / --token / HS2NEXT_TOKEN, else <Bridge folder>/token of the endpoint
found. The app writes a new token at each start: a client that found its endpoint by discovery reads the token again when a
request is refused with 401 and retries once. Bridge.for_user_data(dir) = discovery from that UserData folder (for scripts that
start a player with -userData). A refused request without a token says how to get one.
Always use 127.0.0.1: the listener answers 400 to a Host header of "localhost".

Library:
    from hs2n_bridge import call, batch, Bridge
    call({"cmd": "ping"})                                        # dict; raises BridgeError (a RuntimeError) on ok false
    batch([{"cmd": "chara.load", "path": "x.png", "as": "c"},
           {"cmd": "chara.anime", "key": "$c.added.0.key", "group": 0, "category": 0, "no": 1}])   # list of member replies
    b = Bridge(url="http://127.0.0.1:17321/")                    # HS2's Studio Bridge, same calls
    b.wait_ready(timeout=600)                                    # until ping reports appReady

Command line:
    python Tools/hs2n_bridge.py '{"cmd":"ping"}'
    python Tools/hs2n_bridge.py ping                             # a bare name is {"cmd": name}
    python Tools/hs2n_bridge.py '[{"cmd":"ping"},{"cmd":"help","name":"batch"}]'   # a JSON array is a batch
    python Tools/hs2n_bridge.py -f request.json                  # or - for stdin
    python -I Tools/hs2n_bridge.py --self-test                    # discovery on fixture folders (exit 0 = pass)
    options: --url URL, --hs2 (= http://127.0.0.1:17321/), --userdata DIR, --token T, --timeout S, --retries N,
             --no-check (print failed replies instead of raising), --compact
    exit code: 0 ok, 1 the command failed (ok false), 2 transport error (no bridge, HTTP 4xx)
"""
import http.client
import json
import os
import socket
import sys
import time
import urllib.error
import urllib.request

DEFAULT_URL = "http://127.0.0.1:17322/"
HS2_URL = "http://127.0.0.1:17321/"
TOKEN_HEADER = "X-HS2Next-Token"
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOKEN_HINT = "use discovery (user_data=...) or pass token="

_LOOPBACK = ("127.0.0.1", "localhost", "::1")


class _LoopbackConnection(http.client.HTTPConnection):
    """Connects to a loopback host without a timeout and applies the timeout afterwards. On Windows a timed connect
    (non-blocking connect + select) costs about 14 ms per connection on loopback, a blocking one about 0.14 ms
    (Phase 2.5, player_checks finding F1); a refused loopback connect still fails at once. Other hosts are unchanged."""

    def connect(self):
        if self.host not in _LOOPBACK:
            return super().connect()
        t = self.timeout
        self.timeout = None
        try:
            super().connect()
        finally:
            self.timeout = t
        if t is not socket._GLOBAL_DEFAULT_TIMEOUT:
            self.sock.settimeout(t)


class _LoopbackHandler(urllib.request.HTTPHandler):
    def http_open(self, req):
        return self.do_open(_LoopbackConnection, req)


# no proxy for loopback, whatever HTTP_PROXY says
_opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), _LoopbackHandler())


class BridgeError(RuntimeError):
    """A failed command (ok false) or a refused request. str() = "<cmd> -> <error>" like hs2scene.call."""

    def __init__(self, cmd, message, code=None, detail=None, reply=None, status=None):
        super().__init__("%s -> %s" % (cmd, message))
        self.cmd = cmd
        self.error = message
        self.code = code
        self.detail = detail or {}
        self.reply = reply
        self.status = status


class TransportError(BridgeError):
    """No usable HTTP exchange: connection refused after the retries, or an HTTP 4xx refusal (403, 401, 405, 413)."""


def _bridge_dir_from(path):
    """The Bridge folder for a path that names endpoint.json, the Bridge folder or the UserData folder."""
    p = os.path.abspath(path)
    if os.path.isfile(p):
        return os.path.dirname(p)
    if os.path.isfile(os.path.join(p, "endpoint.json")):
        return p
    return os.path.join(p, "Bridge")


def per_user_data():
    """The UserData folder of a player in the per-user mode (%USERPROFILE%\\AppData\\LocalLow\\stp3\\HS2Next\\UserData), or None."""
    home = os.environ.get("USERPROFILE") or os.path.expanduser("~")
    return os.path.join(home, "AppData", "LocalLow", "stp3", "HS2Next", "UserData") if home else None


def candidates():
    """The UserData folders discovery looks in when none is named: the repository's (the Editor), HS2NEXT_DATA_ROOT's, the
    per-user folder's."""
    out = [os.path.join(REPO, "UserData")]
    root = os.environ.get("HS2NEXT_DATA_ROOT")
    if root and root.strip():
        out.append(os.path.join(os.path.abspath(root.strip()), "UserData"))
    pu = per_user_data()
    if pu:
        out.append(pu)
    seen, uniq = set(), []
    for c in out:
        k = os.path.normcase(os.path.abspath(c))
        if k not in seen:
            seen.add(k)
            uniq.append(c)
    return uniq


def pid_alive(pid):
    """The process exists and has not ended (Windows: OpenProcess + GetExitCodeProcess; elsewhere: signal 0)."""
    try:
        pid = int(pid)
    except (TypeError, ValueError):
        return False
    if pid <= 0:
        return False
    if os.name == "nt":
        import ctypes
        k32 = ctypes.windll.kernel32
        h = k32.OpenProcess(0x1000, False, pid)   # PROCESS_QUERY_LIMITED_INFORMATION
        if not h:
            return ctypes.GetLastError() == 5      # access denied: it exists
        try:
            code = ctypes.c_ulong()
            return bool(k32.GetExitCodeProcess(h, ctypes.byref(code))) and code.value == 259   # STILL_ACTIVE
        finally:
            k32.CloseHandle(h)
    try:
        os.kill(pid, 0)
        return True
    except PermissionError:
        return True
    except OSError:
        return False


def _read_endpoint(bridge_dir):
    """(info, path) of <bridge_dir>/endpoint.json, or (None, path) when absent or unreadable."""
    ep = os.path.join(bridge_dir, "endpoint.json")
    try:
        with open(ep, encoding="utf-8") as f:
            info = json.load(f)
        return (info if isinstance(info, dict) else None), ep
    except (OSError, ValueError):
        return None, ep


def _token_of(bridge_dir, info, token):
    if token is None and info.get("tokenRequired"):
        tp = os.path.join(bridge_dir, "token")
        try:
            with open(tp, encoding="utf-8") as f:
                token = f.read().strip() or None
        except OSError:
            pass
    return token


def discover(user_data=None, use_env=True, folders=None):
    """(url, token, source): the endpoint as the module docstring orders it. use_env=False ignores HS2NEXT_BRIDGE and
    HS2NEXT_USERDATA (Bridge.for_user_data); folders replaces the candidate list (tests)."""
    token = os.environ.get("HS2NEXT_TOKEN") or None
    env = os.environ.get("HS2NEXT_BRIDGE") if use_env and not user_data else None
    if env:
        if env.startswith("http://") or env.startswith("https://"):
            return (env if env.endswith("/") else env + "/"), token, "HS2NEXT_BRIDGE"
        named = [_bridge_dir_from(env)]
    else:
        ud = user_data or (os.environ.get("HS2NEXT_USERDATA") if use_env else None)
        named = [os.path.join(os.path.abspath(ud), "Bridge")] if ud else None
    if named:
        # a folder the caller named: its endpoint, live or not (as before)
        info, ep = _read_endpoint(named[0])
        if info is not None:
            return info.get("url") or DEFAULT_URL, _token_of(named[0], info, token), ep
        return DEFAULT_URL, token, "default"
    best = None
    for ud in (folders if folders is not None else candidates()):
        bd = os.path.join(os.path.abspath(ud), "Bridge")
        info, ep = _read_endpoint(bd)
        if info is None or not pid_alive(info.get("pid")):
            continue
        started = str(info.get("startedAt") or "")
        if best is None or started > best[0]:
            best = (started, bd, info, ep)
    if best is not None:
        _, bd, info, ep = best
        return info.get("url") or DEFAULT_URL, _token_of(bd, info, token), ep
    return DEFAULT_URL, token, "default"


class Bridge:
    """A bridge endpoint. url None = discovery (see the module docstring)."""

    def __init__(self, url=None, token=None, timeout=120, retries=20, user_data=None, _use_env=True):
        self._rediscover = None
        if url:
            self.url, self.token, self.source = (url if url.endswith("/") else url + "/"), token, "argument"
        else:
            self.url, found_token, self.source = discover(user_data, use_env=_use_env)
            self.token = token or found_token
            if not token:
                self._rediscover = (user_data, _use_env)   # the token rotates at each start: read it again on a 401
        self.timeout = timeout
        self.retries = retries

    @classmethod
    def for_user_data(cls, user_data, **kw):
        """The bridge of the app that uses this UserData folder (its endpoint.json and, when required, its token); for scripts
        that start a player with -userData. HS2NEXT_BRIDGE and HS2NEXT_USERDATA do not apply."""
        return cls(user_data=user_data, _use_env=False, **kw)

    def raw(self, request, timeout=None, retries=None):
        """POST one request object (or list = batch); returns the parsed reply without judging ok."""
        if isinstance(request, list):
            request = {"cmd": "batch", "cmds": request}
        cmd = request.get("cmd", "?") if isinstance(request, dict) else "?"
        body = json.dumps(request, ensure_ascii=False).encode("utf-8")
        t = self.timeout if timeout is None else timeout
        if isinstance(request, dict) and isinstance(request.get("timeout"), (int, float)):
            t = max(t, request["timeout"] + 10)   # the bridge answers timeout itself; wait a little longer
        n = self.retries if retries is None else retries
        headers = {"Content-Type": "application/json; charset=utf-8"}
        if self.token:
            headers[TOKEN_HEADER] = self.token
        last = None
        for attempt in range(n + 1):
            try:
                req = urllib.request.Request(self.url, data=body, headers=headers, method="POST")
                with _opener.open(req, timeout=t) as resp:
                    raw = resp.read()
                return json.loads(raw.decode("utf-8"))
            except urllib.error.HTTPError as e:
                text = e.read().decode("utf-8", "replace").strip()
                if e.code == 503:              # shutting down / reloading: retry
                    last = e
                elif e.code == 400:            # bad_json (the body is a JSON error reply)
                    try:
                        return json.loads(text)
                    except ValueError:
                        raise TransportError(cmd, "HTTP 400 %s" % text, status=400)
                elif e.code == 401 and self._rediscover is not None and not getattr(self, "_retried_token", False):
                    # a new start wrote a new token (and perhaps a new endpoint): discover again, once
                    url, tok, src = discover(self._rediscover[0], use_env=self._rediscover[1])
                    self._retried_token = True
                    if tok and (tok != self.token or url != self.url):
                        self.url, self.token, self.source = url, tok, src
                        headers[TOKEN_HEADER] = tok
                        try:
                            return self.raw(request, timeout, retries)
                        finally:
                            self._retried_token = False
                    self._retried_token = False
                    raise TransportError(cmd, "HTTP 401 %s (%s)" % (text, TOKEN_HINT), code="http_401", status=401)
                elif e.code == 401:
                    raise TransportError(cmd, "HTTP 401 %s (%s)" % (text, TOKEN_HINT), code="http_401", status=401)
                else:
                    raise TransportError(cmd, "HTTP %d %s" % (e.code, text), code="http_%d" % e.code, status=e.code)
            except (OSError, ValueError) as e:  # refused, reset, empty reply, timeout (bridge reloading)
                last = e
            if attempt < n:
                time.sleep(0.5)
        raise TransportError(cmd, "no reply from %s after %d attempts: %s" % (self.url, n + 1, last))

    def call(self, cmd, timeout=None, retries=None, check=True):
        """One command; the reply dict. check: raise BridgeError when ok is false."""
        r = self.raw(cmd, timeout, retries)
        if check and not r.get("ok", False):
            name = cmd.get("cmd") if isinstance(cmd, dict) else "batch"
            raise BridgeError(name, r.get("error"), r.get("code"), r.get("detail"), r)
        return r

    def batch(self, cmds, stop_on_error=True, check=True, **envelope):
        """Members in order as one queue entry; returns the list of member replies (raises on failure when check)."""
        req = {"cmd": "batch", "cmds": list(cmds), "stopOnError": stop_on_error}
        req.update(envelope)
        r = self.call(req, check=False)
        if check and not r.get("ok", False):
            err = BridgeError("batch", r.get("error"), r.get("code"), r.get("detail"), r)
            err.failed_at = r.get("failedAt")
            raise err
        return r.get("results", [])

    def ping(self):
        return self.call({"cmd": "ping"})

    def wait_ready(self, timeout=600, poll=0.5, stage=None):
        """Waits until ping reports the app ready (HS2Next appReady, HS2 studio) or one stage; returns the last ping."""
        end = time.time() + timeout
        while True:
            try:
                p = self.call({"cmd": "ping"}, retries=0)
                if stage is not None:
                    if p.get("ready", {}).get(stage):
                        return p
                elif p.get("appReady", p.get("studio")):
                    return p
            except BridgeError:
                pass
            if time.time() > end:
                raise TransportError("ping", "not ready after %s s" % timeout)
            time.sleep(poll)


_default = None


def _bridge(url):
    global _default
    if url:
        return Bridge(url)
    if _default is None:
        _default = Bridge()
    return _default


def call(cmd, timeout=120, retries=20, url=None):
    """One bridge command, hs2scene.call-compatible: retries for ~10 s while the bridge is reloading; raises on ok false."""
    return _bridge(url).call(cmd, timeout=timeout, retries=retries)


def batch(cmds, stop_on_error=True, timeout=120, url=None, **envelope):
    """A batch; the list of member replies."""
    b = _bridge(url)
    req = {"cmd": "batch", "cmds": list(cmds), "stopOnError": stop_on_error}
    req.update(envelope)
    r = b.call(req, timeout=timeout, check=False)
    if not r.get("ok", False):
        err = BridgeError("batch", r.get("error"), r.get("code"), r.get("detail"), r)
        err.failed_at = r.get("failedAt")
        raise err
    return r.get("results", [])


def _main(argv):
    import argparse
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="Send one request to the HS2Next bridge (or HS2's Studio Bridge).")
    ap.add_argument("request", nargs="?", help='JSON object, JSON array (= batch) or a bare command name')
    ap.add_argument("-f", "--file", help="read the request from a file (- = stdin)")
    ap.add_argument("--url")
    ap.add_argument("--hs2", action="store_true", help="target HS2's Studio Bridge (%s)" % HS2_URL)
    ap.add_argument("--userdata")
    ap.add_argument("--token")
    ap.add_argument("--timeout", type=float, default=120)
    ap.add_argument("--retries", type=int, default=20)
    ap.add_argument("--no-check", action="store_true", help="print a failed reply instead of an error message")
    ap.add_argument("--compact", action="store_true")
    ap.add_argument("--self-test", action="store_true", help="check discovery on fixture folders and exit")
    a = ap.parse_args(argv)
    if a.self_test:
        return _self_test()
    if a.file:
        text = sys.stdin.read() if a.file == "-" else open(a.file, encoding="utf-8").read()
    elif a.request is not None:
        text = a.request
    else:
        text = '{"cmd":"ping"}'
    text = text.strip()
    try:
        req = json.loads(text) if text[:1] in "{[" else {"cmd": text}
    except ValueError as e:
        print("not JSON: %s" % e, file=sys.stderr)
        return 2
    b = Bridge(url=HS2_URL if a.hs2 else a.url, token=a.token, timeout=a.timeout, retries=a.retries, user_data=a.userdata)
    try:
        r = b.raw(req)
    except TransportError as e:
        print(str(e), file=sys.stderr)
        return 2
    print(json.dumps(r, ensure_ascii=False, indent=None if a.compact else 2))
    if not r.get("ok", False):
        if not a.no_check:
            print("%s -> %s (%s)" % (r.get("cmd", req.get("cmd") if isinstance(req, dict) else "batch"), r.get("error"), r.get("code")), file=sys.stderr)
        return 1
    return 0


def _self_test():
    """TK5: discovery over fixture folders - the live, newer endpoint wins; an endpoint of an ended process is skipped; the token is
    read when required; a named folder is used as it is; HS2NEXT_TOKEN wins; a 401 without a token names the way to get one and a
    discovered client re-reads a rotated token once. Prints one line per check and "SUMMARY: N PASS, M FAIL"; exit = failures."""
    import http.server
    import shutil
    import subprocess
    import tempfile
    import threading
    results = []

    def check(ok, what):
        results.append(bool(ok))
        print("%s %s" % ("PASS" if ok else "FAIL", what))

    saved = {k: os.environ.get(k) for k in ("HS2NEXT_TOKEN", "HS2NEXT_BRIDGE", "HS2NEXT_USERDATA", "HS2NEXT_DATA_ROOT")}
    for k in saved:
        os.environ.pop(k, None)
    tmp = tempfile.mkdtemp(prefix="hs2n_bridge_selftest_")
    try:
        ended = subprocess.Popen([sys.executable, "-I", "-c", "pass"])
        ended.wait()
        live = os.getpid()

        def endpoint(name, pid, started, required, token=None, url=None):
            bd = os.path.join(tmp, name, "UserData", "Bridge")
            os.makedirs(bd, exist_ok=True)
            with open(os.path.join(bd, "endpoint.json"), "w", encoding="utf-8") as f:
                json.dump({"url": url or "http://127.0.0.1:%d/" % (17600 + len(name)), "pid": pid, "startedAt": started,
                           "tokenRequired": required, "protocol": 1, "app": "HS2Next"}, f)
            if token:
                with open(os.path.join(bd, "token"), "w", encoding="utf-8") as f:
                    f.write(token + "\n")
            return os.path.join(tmp, name, "UserData")

        a = endpoint("a", live, "2026-10-30T08:00:00.000Z", True, "a" * 32)
        b = endpoint("bb", live, "2026-10-30T09:00:00.000Z", False)
        c = endpoint("ccc", ended.pid, "2026-10-30T10:00:00.000Z", False)
        check(pid_alive(live) and not pid_alive(ended.pid), "pid_alive: this process yes, an ended one no")
        url, tok, src = discover(folders=[a, b, c])
        check(url == "http://127.0.0.1:17602/" and tok is None and src.endswith(os.path.join("bb", "UserData", "Bridge", "endpoint.json")),
              "the live, newer endpoint wins over the older one; the newest of an ended process is skipped (%s)" % url)
        url, tok, src = discover(folders=[c, a])
        check(url == "http://127.0.0.1:17601/" and tok == "a" * 32, "the token is read when the endpoint requires one")
        url, tok, src = discover(folders=[c])
        check(url == DEFAULT_URL and src == "default", "only an ended process's endpoint: the default URL")
        url, tok, src = discover(user_data=c, folders=[a, b])
        check(url == "http://127.0.0.1:17603/", "a named UserData is used as it is (live or not)")
        os.environ["HS2NEXT_TOKEN"] = "f" * 32
        url, tok, src = discover(folders=[a])
        check(tok == "f" * 32, "HS2NEXT_TOKEN wins over the token file")
        os.environ.pop("HS2NEXT_TOKEN")
        os.environ["HS2NEXT_DATA_ROOT"] = os.path.join(tmp, "a")
        check(any(os.path.normcase(x) == os.path.normcase(a) for x in candidates()), "HS2NEXT_DATA_ROOT/UserData is a candidate")
        os.environ.pop("HS2NEXT_DATA_ROOT")
        pu = per_user_data() or ""
        check(pu.endswith(os.path.join("AppData", "LocalLow", "stp3", "HS2Next", "UserData")) and pu in candidates(), "the per-user folder is a candidate")
        os.environ["HS2NEXT_BRIDGE"] = "http://127.0.0.1:1/"
        check(Bridge.for_user_data(a).url == "http://127.0.0.1:17601/" and Bridge.for_user_data(a).token == "a" * 32,
              "Bridge.for_user_data: that folder's endpoint and token, HS2NEXT_BRIDGE ignored")
        os.environ.pop("HS2NEXT_BRIDGE")

        # a token-required server: 401 without the right header; the token rotates
        state = {"token": "1" * 32}

        class H(http.server.BaseHTTPRequestHandler):
            def do_POST(self):
                n = int(self.headers.get("Content-Length") or 0)
                self.rfile.read(n)
                if self.headers.get(TOKEN_HEADER) != state["token"]:
                    body = b"unauthorized: header X-HS2Next-Token missing or wrong (see Bridge/token under the user-data folder)"
                    self.send_response(401)
                else:
                    body = b'{"ok":true,"cmd":"ping"}'
                    self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *args):
                pass

        srv = http.server.HTTPServer(("127.0.0.1", 0), H)
        th = threading.Thread(target=srv.serve_forever, daemon=True)
        th.start()
        try:
            surl = "http://127.0.0.1:%d/" % srv.server_address[1]
            d = endpoint("srv", live, "2026-10-30T11:00:00.000Z", True, state["token"], surl)
            try:
                Bridge(url=surl, retries=0).ping()
                check(False, "a URL without a token is refused")
            except TransportError as e:
                check(e.status == 401 and "unauthorized" in str(e) and TOKEN_HINT in str(e), "a URL without a token: the 401 text and the hint (%s)" % e)
            br = Bridge.for_user_data(d, retries=0)
            check(br.ping().get("ok") is True, "discovery sends the token")
            state["token"] = "2" * 32
            with open(os.path.join(d, "Bridge", "token"), "w", encoding="utf-8") as f:
                f.write(state["token"] + "\n")
            check(br.ping().get("ok") is True and br.token == "2" * 32, "a rotated token is read again after one 401")
            state["token"] = "3" * 32
            try:
                br.ping()
                check(False, "a token nobody wrote stays refused")
            except TransportError as e:
                check(e.status == 401 and TOKEN_HINT in str(e), "a token nobody wrote stays refused, with the hint")
        finally:
            srv.shutdown()
            srv.server_close()
    finally:
        for k, v in saved.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v
        shutil.rmtree(tmp, ignore_errors=True)
    fails = results.count(False)
    print("SUMMARY: %d PASS, %d FAIL (hs2n_bridge discovery self-test)" % (len(results) - fails, fails))
    return min(fails, 100)


if __name__ == "__main__":
    sys.exit(_main(sys.argv[1:]))
