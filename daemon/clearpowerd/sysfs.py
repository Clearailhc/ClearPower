"""Tiny sysfs helpers. Every read is best-effort and returns None on failure."""
import os


def read_str(path):
    try:
        with open(path, "r") as f:
            return f.read().strip()
    except OSError:
        return None


def read_int(path):
    s = read_str(path)
    if s is None:
        return None
    try:
        return int(s)
    except ValueError:
        return None


def write_str(path, value):
    """Write and raise OSError with errno on failure (caller reports to UI)."""
    with open(path, "w") as f:
        f.write(str(value))


def read_bracketed(path):
    """Parse 'a [b] c' style sysfs enums -> (current, [choices])."""
    s = read_str(path)
    if s is None:
        return None, []
    choices = []
    current = None
    for tok in s.split():
        if tok.startswith("[") and tok.endswith("]"):
            tok = tok[1:-1]
            current = tok
        choices.append(tok)
    return current, choices


def exists(path):
    return os.path.exists(path)
