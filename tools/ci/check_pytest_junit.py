"""CI guard: mandatory tests must RUN, not silently skip (M4; lead-approved 2026-11-19 with the Shapely pin).

    python tools/ci/check_pytest_junit.py <junit.xml> [--allow-skips] [--require-module NAME ...]

Fails (exit 1) when the JUnit report
- contains any failed or errored test case,
- contains any skipped test case (unless --allow-skips), or
- has no PASSED test case for a required module (for example test_d02_parcel), which also catches a module that
  was skipped at import time or not collected at all.

Prints counts and the names of offending modules only, never test output.
"""
import argparse
import collections
import sys
import xml.etree.ElementTree as ElementTree


def outcome(case) -> str:
    if case.find("failure") is not None or case.find("error") is not None:
        return "failed"
    if case.find("skipped") is not None:
        return "skipped"
    return "passed"


def module_of(case) -> str:
    # pytest classname: "tests.m6_contract.test_d02_parcel" or "...test_d02_parcel.TestClass". A module skipped at
    # import time is recorded with an EMPTY classname and the dotted module path as its name.
    parts = (case.get("classname") or case.get("name") or "").split(".")
    return next((p for p in reversed(parts) if p.startswith("test_")), parts[-1] if parts else "")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("junit")
    parser.add_argument("--allow-skips", action="store_true")
    parser.add_argument("--require-module", action="append", default=[])
    args = parser.parse_args(argv)

    cases = list(ElementTree.parse(args.junit).getroot().iter("testcase"))
    totals = collections.Counter(outcome(c) for c in cases)
    passed_by_module = collections.Counter(module_of(c) for c in cases if outcome(c) == "passed")
    skipped_modules = sorted({module_of(c) for c in cases if outcome(c) == "skipped"})

    print(f"tests={len(cases)} passed={totals['passed']} failed={totals['failed']} skipped={totals['skipped']}")
    for name in args.require_module:
        print(f"  {name}: passed={passed_by_module[name]}")

    problems = []
    if not cases:
        problems.append("no test cases were recorded")
    if totals["failed"]:
        problems.append(f"{totals['failed']} failed or errored test case(s)")
    if totals["skipped"] and not args.allow_skips:
        problems.append(f"{totals['skipped']} skipped test case(s) in: {', '.join(skipped_modules)}")
    missing = [name for name in args.require_module if passed_by_module[name] == 0]
    if missing:
        problems.append(f"required module(s) ran no passing tests: {', '.join(missing)}")

    for problem in problems:
        print(f"MANDATORY TEST GUARD: {problem}", file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
