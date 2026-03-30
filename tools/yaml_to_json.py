import json, sys
from pathlib import Path
import yaml  # pip install pyyaml

yaml_path = Path(sys.argv[1])
json_path = Path(sys.argv[2])

data = yaml.safe_load(yaml_path.read_text(encoding="utf-8"))
# This helper keeps the original YAML structure intact; the wrapped converter is used for Unity.
json_path.write_text(json.dumps(data, indent=2), encoding="utf-8")
print(f"Wrote {json_path}")
