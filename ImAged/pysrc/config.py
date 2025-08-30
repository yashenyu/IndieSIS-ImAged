import json, sys, os
from pathlib import Path

def resource_path(*parts):
    if getattr(sys, 'frozen', False) and hasattr(sys, '_MEIPASS'):
        base = Path(sys._MEIPASS)
    else:
        base = Path(__file__).parent
    return base.joinpath(*parts)

CONFIG_PATH = resource_path("config", "config.json")

def load_config() -> dict:
    if CONFIG_PATH.exists():
        cfg = json.loads(CONFIG_PATH.read_text())
        validate_config(cfg)
        return cfg
    return {}

def validate_config(config: dict):
    required_fields = ["ntp_server", "default_ttl_hours"]
    
    for field in required_fields:
        if field not in config:
            raise ValueError(f"Missing required configuration field: {field}")
    
    # Validate NTP server configuration
    ntp_server = config.get("ntp_server")
    if not ntp_server or not isinstance(ntp_server, str):
        raise ValueError("ntp_server must be a non-empty string")
    
    # Validate TTL hours configuration
    ttl_hours = config.get("default_ttl_hours")
    if not isinstance(ttl_hours, (int, float)) or ttl_hours <= 0:
        raise ValueError("default_ttl_hours must be a positive number")

def save_config(cfg: dict):
    validate_config(cfg)
    appdir = Path(os.environ.get("APPDATA", str(Path.home() / "AppData" / "Roaming"))) / "ImAged"
    appdir.mkdir(parents=True, exist_ok=True)
    (appdir / "config.json").write_text(json.dumps(cfg, indent=2))