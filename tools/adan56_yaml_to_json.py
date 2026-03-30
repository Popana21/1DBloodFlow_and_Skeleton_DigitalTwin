import json, sys
from pathlib import Path

try:
    import yaml
except ImportError:
    print("Missing dependency: PyYAML. Install with: pip install pyyaml")
    sys.exit(1)

def main():
    if len(sys.argv) != 3:
        print("Usage: python yaml_to_vessels_json.py <input.yaml> <output vessels.json>")
        sys.exit(2)

    yaml_in = Path(sys.argv[1])
    out_json = Path(sys.argv[2])

    if not yaml_in.exists():
        raise FileNotFoundError(f"YAML file not found: {yaml_in}")

    data = yaml.safe_load(yaml_in.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise RuntimeError("Unexpected YAML root type (expected dict).")

    # Accept a few likely schema variants so the converter survives small YAML layout changes.
    # Locate vessel list
    vessels_src = None
    for k in ["vessels", "Vessels", "network", "Network", "vascular_network", "model"]:
        if k in data:
            vessels_src = data[k]
            break

    if isinstance(vessels_src, dict):
        for k in ["vessels", "Edges", "edges", "links"]:
            if k in vessels_src:
                vessels_src = vessels_src[k]
                break

    if vessels_src is None:
        raise RuntimeError("Could not locate vessels in YAML under expected keys.")

    vessels_out = []

    if isinstance(vessels_src, dict):
        for label, v in vessels_src.items():
            if not isinstance(v, dict):
                continue
            vessels_out.append({
                "label": str(label),
                "sn": int(v.get("sn", v.get("start", v.get("from", 0)))),
                "tn": int(v.get("tn", v.get("end", v.get("to", 0)))),
                "L": float(v.get("L", v.get("length", 0.0))),
                "E": float(v.get("E", v.get("elasticity", 0.0))),
                "M": int(v.get("M", v.get("segments", 0))),
                "Rp": float(v.get("Rp", v.get("Rp_prox", 0.0))),
                "Rd": float(v.get("Rd", v.get("Rd_dist", 0.0))),
                "gamma_profile": float(v.get("gamma_profile", 0)),
                "Pext": float(v.get("Pext", 0.0)),
            })

    elif isinstance(vessels_src, list):
        for v in vessels_src:
            if not isinstance(v, dict):
                continue
            label = v.get("label", v.get("name", "unnamed"))
            vessels_out.append({
                "label": str(label),
                "sn": int(v.get("sn", v.get("start", v.get("from", 0)))),
                "tn": int(v.get("tn", v.get("end", v.get("to", 0)))),
                "L": float(v.get("L", v.get("length", 0.0))),
                "E": float(v.get("E", v.get("elasticity", 0.0))),
                "M": int(v.get("M", v.get("segments", 0))),
                "Rp": float(v.get("Rp", v.get("Rp_prox", 0.0))),
                "Rd": float(v.get("Rd", v.get("Rd_dist", 0.0))),
                "gamma_profile": float(v.get("gamma_profile", 0)),
                "Pext": float(v.get("Pext", 0.0)),
            })
    else:
        raise RuntimeError(f"Unsupported vessels container type: {type(vessels_src)}")

    # Unity's JsonUtility expects a wrapped root object rather than a top-level array.
    payload = {"vessels": vessels_out}
    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_json.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    print(f"✔ Wrote vessels.json with {len(vessels_out)} vessels to: {out_json}")

if __name__ == "__main__":
    main()
