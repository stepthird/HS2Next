"""
script_census.py - the command forms of scene scripts (`hs2next.script` documents), and whether a script covers a list of forms.

A form is a command and the shapes of its arguments: each argument's name with "num", "bool", "str", "$role" (a string starting with $),
"list[n]" (an array of n elements) or "{a,b}" (an object with those member names, sorted); `note` members are not counted. A wait step
is the form "(wait)". The forms are read from a script's set-up and preparation steps and from its timed commands (the `do` of every
event). Standard library only; no app is needed.

Usage:
    python -I Tools/script_census.py <script> [<script> ...]                                     # the forms of the scripts
    python -I Tools/script_census.py <script> --census <census.json> [--forms <forms.json>] [--floor N]   # coverage (JV4)

--census: a census of command forms, {"commands": {<cmd>: {"forms": [{"args": {<name>: <shape>}, "n": uses}]}}}.
--forms: for census forms written in another command vocabulary, the requests each became:
    {"forms": [{"census": {"cmd", "args"}, "converted": [<request or wait step>, ...]}]};
    a census form is covered when the script holds the form of every request listed for it. Without --forms a census form is covered
    when the script holds that very form.
Prints one line per uncovered form, the script's forms outside the census, and a verdict line
"PASS|FAIL JV4 the script covers N of M census forms (Present, floor F)"; exit 0 covered, 1 not covered, 2 setup error.
"""
import argparse
import json
import sys

WAIT = "(wait)"


def shape(v):
    if isinstance(v, bool):
        return "bool"
    if isinstance(v, (int, float)):
        return "num"
    if isinstance(v, str):
        return "$role" if v.startswith("$") else "str"
    if isinstance(v, list):
        return "list[%d]" % len(v)
    if isinstance(v, dict):
        return "{" + ",".join(sorted(v.keys())) + "}"
    return type(v).__name__


def form_of(step):
    """(cmd, {arg: shape}) of a request or a wait step; None for anything else."""
    if not isinstance(step, dict):
        return None
    if "cmd" not in step:
        if "wait" in step:
            return WAIT, {k: shape(v) for k, v in step.items() if k != "note"}
        return None
    return step["cmd"], {k: shape(v) for k, v in step.items() if k not in ("cmd", "note")}


def key_of(cmd, args):
    return json.dumps([cmd, dict(sorted(args.items()))], ensure_ascii=False, sort_keys=True)


def text_of(cmd, args):
    return "%s %s" % (cmd, json.dumps(dict(sorted(args.items())), ensure_ascii=False))


def script_forms(doc):
    """{form key: (cmd, args, uses, places)} of a script document."""
    found = {}

    def add(step, place):
        f = form_of(step)
        if f is None:
            return
        k = key_of(*f)
        if k not in found:
            found[k] = [f[0], f[1], 0, []]
        found[k][2] += 1
        if place not in found[k][3]:
            found[k][3].append(place)

    for phase in ("setup", "prepare"):
        for s in doc.get(phase) or []:
            add(s, phase)
    for e in (doc.get("timeline") or {}).get("events") or []:
        if isinstance(e, dict):
            add(e.get("do"), "event")
    return found


def read_json(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description="command forms of scene scripts")
    ap.add_argument("scripts", nargs="+")
    ap.add_argument("--census")
    ap.add_argument("--forms")
    ap.add_argument("--floor", type=int, default=None, help="the least number of census forms to cover (default: all of them)")
    a = ap.parse_args()

    found = {}
    for p in a.scripts:
        try:
            doc = read_json(p)
        except (OSError, ValueError) as e:
            print("setup: %s: %s" % (p, e))
            return 2
        if doc.get("format") != "hs2next.script":
            print("setup: %s is not a scene script (format %r)" % (p, doc.get("format")))
            return 2
        for k, v in script_forms(doc).items():
            if k in found:
                found[k][2] += v[2]
                found[k][3] = sorted(set(found[k][3]) | set(v[3]))
            else:
                found[k] = v
    print("scripts: %s; %d forms of %d commands" % (", ".join(a.scripts), len(found), len(set(v[0] for v in found.values()))))

    if not a.census:
        for k, (cmd, args, n, places) in sorted(found.items(), key=lambda x: (x[1][0], x[0])):
            print("  %3d  %-9s %s" % (n, ",".join(places), text_of(cmd, args)))
        return 0

    try:
        census = read_json(a.census)
        mapping = read_json(a.forms) if a.forms else None
    except (OSError, ValueError) as e:
        print("setup: %s" % e)
        return 2
    wanted = []
    for cmd, cv in (census.get("commands") or {}).items():
        for f in cv.get("forms") or []:
            wanted.append((cmd, f.get("args") or {}, f.get("n")))
    if not wanted:
        print("setup: the census lists no form")
        return 2
    conv = {}
    if mapping is not None:
        for r in mapping.get("forms") or []:
            c = r.get("census") or {}
            conv[key_of(c.get("cmd"), c.get("args") or {})] = [form_of(x) for x in r.get("converted") or [] if form_of(x) is not None]

    covered, used = 0, set()
    missing = []
    for cmd, args, n in wanted:
        k = key_of(cmd, args)
        targets = conv.get(k) if mapping is not None else [(cmd, args)]
        if mapping is not None and not targets:
            missing.append("%s: the forms file names no request for it" % text_of(cmd, args))
            continue
        lack = [t for t in targets if key_of(*t) not in found]
        for t in targets:
            used.add(key_of(*t))
        if lack:
            missing.append("%s -> not in the script: %s" % (text_of(cmd, args), "; ".join(text_of(*t) for t in lack)))
        else:
            covered += 1
    floor = a.floor if a.floor is not None else len(wanted)
    via = " through %s" % a.forms if a.forms else ""
    print("census: %s, %d forms of %d commands%s (%d forms of the script stand for them)" % (a.census, len(wanted), len(set(w[0] for w in wanted)), via, len(used)))
    for m in missing:
        print("  missing  " + m)
    extra = [v for k, v in sorted(found.items()) if k not in used]
    for cmd, args, n, places in extra:
        print("  outside  %s (%d, %s)" % (text_of(cmd, args), n, ",".join(places)))
    ok = covered >= floor and not (a.floor is None and missing)
    print("%s JV4 the script covers %d of %d census forms (Present, floor %d); %d forms of the script are outside the census"
          % ("PASS" if ok else "FAIL", covered, len(wanted), floor, len(extra)))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
