"""Writes Reference/COMMANDS.md, the bridge's command reference, from a help dump (Unity-free, deterministic).

The dump is what Editor/BridgeHelpDump.cs writes (Spike/out/bridge/help_dump.json): the protocol, the count, every command's
`help {name}` object (canonical JSON) and the HS2-parity names. The reference holds a fixed part (how to drive HS2Next from a
script: transport, envelope, token, endpoint discovery, error codes; what the 1.x versions keep stable; the naming
convention), a table of the help groups, and per group per command: summary, flags, owner, since, aliases, the HS2-parity
and experimental marks, gate, `requires`, the default timeout, the arguments (name, type, required, default, doc), the
returned fields (name, type, always, doc), `returnsDoc` and the examples. A command whose flags hold `experimental` is marked
in its heading and in the group table and counted in the summary line. Rerun after any command change:

    & Tools\\unity_batch.ps1 -Method BridgeHelpDump.Run -NoGraphics
    python -I Tools/gen_commands_md.py

Usage:  python -I Tools/gen_commands_md.py [--dump FILE] [--out FILE] [--check]
  --dump    the help dump (default Spike/out/bridge/help_dump.json)
  --out     the reference (default Reference/COMMANDS.md)
  --check   compare instead of writing: exit 0 when FILE equals what the dump gives, 1 otherwise (the first differing line).
Exit code: 0 = written / equal; 1 = differs (--check); 2 = the dump cannot be read.
"""
import argparse
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

PROJ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_DUMP = os.path.join(PROJ, "Spike", "out", "bridge", "help_dump.json")
DEFAULT_OUT = os.path.join(PROJ, "Reference", "COMMANDS.md")

EXPERIMENTAL_FLAG = "experimental"

SCRIPTING = """\
HS2Next listens for automation requests while it runs (the Editor's play mode and every player): HTTP on `127.0.0.1`,
port 17322 by default (setting `bridge.port`, command line `-bridgePort N`). Only programs on the same computer can connect.
Use `127.0.0.1` in URLs: a `Host: localhost` header is answered with 400. The bridge is on by default and can be switched off
in File > Options > Automation, with `"bridge": {"enabled": false}` in the settings or with `-noBridge`.

**Requests.** `POST /` with a JSON object `{"cmd": "<name>", ...arguments}` (UTF-8, at most 16 MiB). An empty body is a
`ping`. Every reply is HTTP 200 with `{"ok": true, "cmd": "<name>", ...returned fields}` or
`{"ok": false, "error": "<message>", "code": "<code>", "detail": {...}}`; scripts branch on `code`, never on the message
text. The transport refuses a request before any command runs with an HTTP status: 400 (the body is not a JSON object),
401 (token missing or wrong), 403 (a browser request: any `Origin` or `Sec-Fetch-*` header, whatever its value; or a remote
address), 404 (a path other than `/`), 405 (a method other than POST), 413 (a body over 16 MiB). Commands run in order on
the app's main thread, one queue for all clients; `ping`, `help`, `bridge.version` and `log.tail` sent alone are answered at
once, also while the main thread is busy.

**Envelope members** that any request may carry besides the command's arguments: `id` (any JSON value, echoed in the reply),
`timeout` (seconds, at most 600; default 60 or the command's own), `waitFrames` (frames to wait after the command before the
reply), `strict` (`true`: an unknown argument is an error instead of a warning), `undo` (`false`: a change is made without an
undo entry) and, inside a `batch`, `as` (names a member's reply; later members refer to it as `"$name.field"`).

**Token.** The release build requires a token by default: send the header `X-HS2Next-Token` with the content of the file
`<UserData>/Bridge/token`, which the app writes anew at each start. Development builds and the Editor require none unless the
setting `bridge.token` asks for one.

**Finding a running app.** While it listens, the app writes `<UserData>/Bridge/endpoint.json` with `url`, `pid`, `startedAt`
and `tokenRequired`, and removes it when it stops. `<UserData>` is `%USERPROFILE%\\AppData\\LocalLow\\stp3\\HS2Next\\UserData`
for an installed player, the `UserData` folder next to `HS2Next.exe` in portable mode, and the project's `UserData/` in the
Editor; `app.paths` reports the folders of the running app.

**First steps.** `ping` reports the app's readiness stages (wait until it is ready before loading anything); `help` lists the
groups and commands; `{"cmd": "help", "name": "<command>"}` returns one command's description as below; `bridge.version`
gives the protocol, Api and app versions. `batch` runs a list of requests in order and returns their replies.
`Tools/hs2n_bridge.py` is a small client (library and command line, Python standard library only) that finds the endpoint and
the token by the rules above:

```
python Tools/hs2n_bridge.py ping
python Tools/hs2n_bridge.py '{"cmd": "help", "name": "scene.load"}'
python Tools/hs2n_bridge.py '[{"cmd": "scene.new"}, {"cmd": "map.load", "no": 10}]'
```

**Error codes.**

| Code | Meaning |
|---|---|
| `bad_json` | the body is not a JSON object |
| `unknown_command` | no command of that name (`detail.candidates`: the nearest names) |
| `bad_args` | an argument is missing, of the wrong type or out of range (`detail.arg`, `detail.expected`) |
| `not_found` | a named thing (node, file, list row, help entry) does not exist |
| `wrong_kind` | the target exists but is of another kind (an item key given to a character command) |
| `not_ready` | the app is not ready for this command yet (`detail.extra.ready`: the stages) |
| `busy` | the bridge cannot take the request now (queue full, a re-entrant call) |
| `timeout` | the request did not complete within its timeout |
| `io_denied` | a file path points outside the allowed folders, or would overwrite a file without `overwrite` |
| `unsupported` | the feature is not available in this version or is switched off |
| `plugin_fault` | a plugin's handler failed |
| `internal` | a built-in handler failed unexpectedly |
| `unavailable` | the target exists but is not built (a character whose build failed or is pending) |

**What stays stable.** The protocol number changes only when the envelope changes. Within the 1.x versions every command
that is not marked *experimental* keeps its name, its arguments (name, type, whether required, declared default), its
returned fields and its flags: a new version may add commands, optional arguments and returned fields, and a renamed
command keeps its old name as an alias. Experimental commands (UI introspection, dumps, diagnostics) may change or go in any
version. Help texts are descriptions, not part of the promise, and error codes may be added but never change meaning.
Plugins add commands of their own; `help` lists them with the plugin's GUID as owner (see `Reference/PLUGINS.md`)."""

CONVENTION = """\
- A name is `<group>.<verb or noun>` in camelCase (`chara.edit.hairBundle`, `scene.load`); the group is the subject: `scene`,
  `obj` (any node), `chara`, `chara.edit` (the Maker), `item`, `map`, `camera`, `light`, `route`, `fk`, `ik`, `pose`, `anime`,
  `effects`, `sound`, `content`, `app`, `ui`, `plugin`, `bridge`, `log`.
- Reads are `state`, `get`, `list` or a noun; writes are `set` or a verb.
- **HS2-parity names** (marked *HS2 parity* below) keep HS2 Studio's established automation vocabulary: they are never renamed
  and keep HS2's argument names, defaults, reply members and number forms.
- The one-word names registered before this convention was written down stay as they are; new Core names follow the
  convention.
- A plugin's command names start with one of its manifest's `commandPrefixes`; its owner and help group are the plugin's GUID.
- A rename adds the old name as an alias (`CommandSpec.Alias`): the old name still reaches the command and its reply carries
  the warning `deprecated: use <name>`. No command has been renamed so far."""


def load_dump(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError) as e:
        print("[gen_commands_md] cannot read the dump %s: %s" % (path, e))
        sys.exit(2)


def prose(text):
    """Text with '<' escaped outside code spans, so that placeholders such as <UserData> are not read as HTML tags."""
    parts = str(text).split("`")
    for i in range(0, len(parts), 2):
        parts[i] = parts[i].replace("<", "\\<")
    return "`".join(parts)


def cell(text):
    """One table cell: no line breaks, pipes escaped, '<' escaped outside code spans."""
    if text is None:
        return ""
    s = prose(str(text).replace("\r\n", " ").replace("\n", " ")).replace("|", "\\|")
    return s.strip()


def code_cell(text):
    """The inside of a code span in a table cell: no line breaks, pipes escaped (nothing else is read inside code)."""
    if text is None:
        return ""
    return str(text).replace("\r\n", " ").replace("\n", " ").replace("|", "\\|").strip()


def jtext(v):
    return json.dumps(v, ensure_ascii=False, separators=(", ", ": "))


def anchor(name):
    return "cmd-" + name.replace(".", "-").lower()


def is_experimental(c):
    """True when the command's flags hold the experimental mark (compared case-insensitively)."""
    return any(str(f).lower() == EXPERIMENTAL_FLAG for f in (c.get("flags") or []))


def render(dump):
    cmds = sorted(dump.get("commands", []), key=lambda c: c["name"])
    parity = set(dump.get("parity") or [])
    groups = {}
    for c in cmds:
        groups.setdefault(c.get("group") or "", []).append(c)
    owners = {}
    for c in cmds:
        owners[c.get("owner")] = owners.get(c.get("owner"), 0) + 1
    n_exp = len([c for c in cmds if is_experimental(c)])
    out = []
    w = out.append
    w("# HS2Next bridge commands")
    w("")
    w("Generated by `Tools/gen_commands_md.py` from a `help` dump (`Editor/BridgeHelpDump.cs` →")
    w("`Spike/out/bridge/help_dump.json`); do not edit by hand. To refresh it after a command change:")
    w("")
    w("```")
    w("& Tools\\unity_batch.ps1 -Method BridgeHelpDump.Run -NoGraphics")
    w("python -I Tools/gen_commands_md.py")
    w("```")
    w("")
    owner_text = ", ".join("%d %s" % (owners[o], o) for o in sorted(owners, key=lambda o: (o != "core", o)))
    w("Protocol %s; %d commands (%s); %d HS2-parity names%s. `help` and `help {name}` answer the same facts at run time."
      % (dump.get("protocol"), len(cmds), owner_text, len([c for c in cmds if c["name"] in parity]),
         "; %d experimental" % n_exp if n_exp else ""))
    w("")
    w("## Driving HS2Next from a script")
    w("")
    w(SCRIPTING)
    w("")
    w("## Naming convention")
    w("")
    w(CONVENTION)
    w("")
    w("## Groups")
    w("")
    w("The help group is what `help {group}` lists (the command file's group, not always the name's first part).")
    if n_exp:
        w("Experimental commands are marked *(experimental)*.")
    w("")
    w("| Help group | Commands | Names |")
    w("|---|---|---|")
    for g in sorted(groups):
        names = ", ".join("[`%s`](#%s)%s" % (c["name"], anchor(c["name"]), " *(experimental)*" if is_experimental(c) else "")
                          for c in groups[g])
        w("| %s | %d | %s |" % (cell(g), len(groups[g]), names))
    w("")
    for g in sorted(groups):
        w("## Group: %s" % g)
        w("")
        for c in groups[g]:
            render_command(w, c, c["name"] in parity)
    text = "\n".join(out).rstrip("\n") + "\n"
    return text


def render_command(w, c, is_parity):
    name = c["name"]
    w('<a id="%s"></a>' % anchor(name))
    w("")
    w("### `%s`%s%s" % (name, " · *HS2 parity*" if is_parity else "", " · *experimental*" if is_experimental(c) else ""))
    w("")
    if c.get("summary"):
        w(prose(c["summary"].strip()))
        w("")
    facts = []
    flags = c.get("flags") or []
    facts.append("flags " + (", ".join("`%s`" % f for f in flags) if flags else "none"))
    facts.append("owner `%s`" % c.get("owner"))
    facts.append("since %s" % c.get("since"))
    if c.get("aliases"):
        facts.append("aliases " + ", ".join("`%s`" % a for a in c["aliases"]))
    if c.get("gate") and c.get("gate") != "app":
        facts.append("gate `%s`" % c["gate"])
    if c.get("requires"):
        facts.append("requires " + ", ".join("`%s`" % r for r in c["requires"]))
    t = c.get("defaultTimeoutSec")
    if t is not None:
        facts.append("timeout %s s" % (int(t) if float(t).is_integer() else t))
    if c.get("allowExtraArgs"):
        facts.append("extra arguments allowed")
    if c.get("handlesWaitFrames"):
        facts.append("handles `waitFrames` itself")
    w("- " + "; ".join(facts))
    w("")
    args = c.get("args") or []
    if args:
        w("| Argument | Type | Required | Default | Description |")
        w("|---|---|---|---|---|")
        for a in args:
            default = jtext(a["default"]) if "default" in a else ""
            w("| `%s` | `%s` | %s | %s | %s |" % (a["name"], code_cell(a.get("type")), "yes" if a.get("required") else "",
                                               ("`%s`" % code_cell(default)) if default else "", cell(a.get("doc"))))
        w("")
    else:
        w("No arguments.")
        w("")
    rets = c.get("returns") or []
    if rets:
        w("| Returns | Type | Always | Description |")
        w("|---|---|---|---|")
        for r in rets:
            w("| `%s` | `%s` | %s | %s |" % (r["name"], code_cell(r.get("type")), "yes" if r.get("always") else "", cell(r.get("doc"))))
        w("")
    if c.get("returnsDoc"):
        w(prose(c["returnsDoc"].strip()))
        w("")
    for ex in c.get("examples") or []:
        w("Example: `%s`" % (jtext(ex) if not isinstance(ex, str) else ex))
        w("")


def main():
    ap = argparse.ArgumentParser(description="Writes Reference/COMMANDS.md from a help dump.")
    ap.add_argument("--dump", default=DEFAULT_DUMP)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--check", action="store_true", help="compare with --out instead of writing it")
    a = ap.parse_args()
    dump = load_dump(a.dump)
    text = render(dump)
    if a.check:
        try:
            with open(a.out, encoding="utf-8", newline="") as f:
                have = f.read()
        except OSError as e:
            print("[gen_commands_md] DIFFERS: cannot read %s (%s)" % (a.out, e))
            return 1
        if have == text:
            print("[gen_commands_md] OK: %s equals the dump's reference (%d lines)" % (a.out, text.count("\n")))
            return 0
        x, y = have.split("\n"), text.split("\n")
        i = 0
        while i < len(x) and i < len(y) and x[i] == y[i]:
            i += 1
        print("[gen_commands_md] DIFFERS at line %d" % (i + 1))
        print("  file: " + (x[i] if i < len(x) else "(end)")[:200])
        print("  dump: " + (y[i] if i < len(y) else "(end)")[:200])
        return 1
    folder = os.path.dirname(os.path.abspath(a.out))
    if not os.path.isdir(folder):
        os.makedirs(folder)
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print("[gen_commands_md] wrote %s (%d lines, %d commands, %d experimental)"
          % (a.out, text.count("\n"), len(dump.get("commands", [])), len([c for c in dump.get("commands", []) if is_experimental(c)])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
