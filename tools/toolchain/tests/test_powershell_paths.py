"""-Path and positional paths read `[`, `]`, `*` and `?` as wildcards, so a player's folder named
`Тест [1]` breaks them; -LiteralPath does not. Resolve-Path has the same problem and is not needed.
Expand-Archive checks an existing -DestinationPath with wildcards (then tries to create the folder and
fails), and Start-Process -WorkingDirectory does not find a bracketed folder, in both editions."""
import re
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[2]
CMDLETS = ("Test-Path", "Get-ChildItem", "Get-Item", "Remove-Item", "Copy-Item", "Move-Item", "Get-Content",
           "Set-Content", "Push-Location", "Set-Location", "Get-FileHash", "Select-String")
_CMDLET = re.compile(r"(?<![\w-])(" + "|".join(map(re.escape, CMDLETS)) + r")(?![\w-])", re.IGNORECASE)
_TOKEN = re.compile(r"""'[^']*'|"[^"]*"|\([^()]*(?:\([^()]*\)[^()]*)*\)|\S+""")


def logical_lines(text):
    """(first line number, line) with backtick continuations joined and comment lines dropped."""
    lines = text.splitlines()
    index = 0
    while index < len(lines):
        start, line = index + 1, lines[index]
        while line.rstrip().endswith("`") and index + 1 < len(lines):
            index += 1
            line = line.rstrip()[:-1] + " " + lines[index].strip()
        index += 1
        if not line.lstrip().startswith("#"):
            yield start, line


def _path_argument(segment):
    tokens = _TOKEN.findall(segment)
    for position, token in enumerate(tokens):
        if token.lower() == "-path" and position + 1 < len(tokens):
            return tokens[position + 1]
    if tokens and not tokens[0].startswith("-"):
        return tokens[0]
    return None


def _wildcard_prone(argument):
    if argument.startswith(("$", "(", "@")) or (argument.startswith('"') and "$" in argument):
        return True
    return any(character in argument for character in "[*?")


def violations(text):
    found = []
    for number, line in logical_lines(text):
        for banned in ("Resolve-Path", "Expand-Archive"):
            if re.search(rf"(?<![\w-]){banned}(?![\w-])", line, re.IGNORECASE):
                found.append((number, banned))
        if re.search(r"Start-Process\b.*-WorkingDirectory\b", line, re.IGNORECASE):
            found.append((number, "Start-Process -WorkingDirectory"))
        for match in _CMDLET.finditer(line):
            segment = re.split(r"[|;{}]", line[match.end():], maxsplit=1)[0]
            if re.search(r"-LiteralPath\b", segment, re.IGNORECASE):
                continue
            argument = _path_argument(segment)
            if argument is not None and _wildcard_prone(argument):
                found.append((number, f"{match.group(1)} {argument}"))
    return found


class ScannerTest(unittest.TestCase):
    def test_variable_and_expression_paths_are_found(self):
        self.assertEqual([n for n, _ in violations(
            "Push-Location $root\n"
            "Test-Path -Path (Join-Path $a 'b')\n"
            "Get-Content \"$dir\\x.txt\" -Raw\n"
            "$p = (Resolve-Path $x).Path\n"
            "Remove-Item `\n    $staging -Recurse\n"
            "Expand-Archive -LiteralPath $zip -DestinationPath $root -Force\n"
            "Start-Process -FilePath $exe `\n    -WorkingDirectory $dir\n")], [1, 2, 3, 4, 5, 7, 8])

    def test_literal_paths_and_constants_pass(self):
        self.assertEqual(violations(
            "Test-Path variable:PSNativeCommandUseErrorActionPreference\n"
            "Remove-Item -LiteralPath $x -Force\n"
            "Get-ChildItem -LiteralPath $root -Filter '*.dll' -File\n"
            "Copy-Item -LiteralPath $a -Destination $b\n"
            "# Push-Location $root in a comment\n"), [])


class ScriptsTest(unittest.TestCase):
    def test_tools_scripts_take_player_paths_literally(self):
        found = [f"{script.name}:{number}: {what}" for script in sorted(TOOLS.glob("*.ps1"))
                 for number, what in violations(script.read_text(encoding="utf-8-sig"))]
        self.assertEqual(found, [])
