import importlib.util
from pathlib import Path

path = Path(__file__).with_name("test_core.py")
spec = importlib.util.spec_from_file_location("test_core", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

failures = []
for name in sorted(dir(module)):
    if name.startswith("test_"):
        try:
            getattr(module, name)()
            print(f"PASS {name}")
        except Exception as exc:
            failures.append((name, exc))
            print(f"FAIL {name}: {exc}")
if failures:
    raise SystemExit(1)
print(f"PASS total={len([n for n in dir(module) if n.startswith('test_')])}")
