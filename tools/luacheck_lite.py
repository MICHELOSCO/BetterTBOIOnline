"""Very small structural sanity check for a Lua file (no interpreter available on this PC):
block keyword balance (function/if/for/while/do vs end), parentheses and braces. Not a parser.
usage: python tools/luacheck_lite.py <file.lua>
"""
import re
import sys

src = open(sys.argv[1], encoding="utf-8").read()
code = re.sub(r"--[^\n]*", "", src)                      # strip comments
code = re.sub(r'"(?:\\.|[^"\\])*"', '""', code)          # blank string literals
code = re.sub(r"'(?:\\.|[^'\\])*'", "''", code)

openers = len(re.findall(r"\bfunction\b", code)) + len(re.findall(r"\bif\b", code))
openers += len(re.findall(r"\bdo\b", code))              # for/while ... do, and plain do
ends = len(re.findall(r"\bend\b", code))
print(f"block openers: {openers}  ends: {ends}  -> {'OK' if openers == ends else 'MISMATCH'}")
for a, b in ("()", "{}", "[]"):
    print(f"{a}{b}: {code.count(a)} / {code.count(b)}  -> {'OK' if code.count(a) == code.count(b) else 'MISMATCH'}")
