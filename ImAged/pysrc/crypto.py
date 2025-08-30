import logging
from pathlib import Path
import os, sys
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.kdf.hkdf import HKDF

def resource_path(*parts):
    if getattr(sys, 'frozen', False) and hasattr(sys, '_MEIPASS'):
        base = Path(sys._MEIPASS)
    else:
        base = Path(__file__).parent
    return base.joinpath(*parts)

def _master_key_path() -> Path:
    return resource_path("config", "master.key")

def _load_or_create_static_master_key() -> bytes:
    path = _master_key_path()
    try:
        if path.exists():
            raw = path.read_bytes()
            key = raw if len(raw) == 32 else raw[:32]
            if len(key) != 32:
                raise ValueError(f"master.key must be 32 bytes (got {len(key)})")
            logging.info("Loaded static MASTER_KEY from %s", path)
            return key
        # Generate a default in memory only (don’t write inside bundle)
        import os
        key = os.urandom(32)
        logging.warning("master.key not found; generated ephemeral key")
        return key
    except Exception as e:
        logging.error("Failed to load/create static master key: %s", e)
        raise

MASTER_KEY = _load_or_create_static_master_key()

def generate_salt(length: int = 16) -> bytes:
    return os.urandom(length)

def derive_cek(salt: bytes, length: int = 32) -> bytes:
    hkdf = HKDF(
        algorithm=hashes.SHA256(),
        length=length,
        salt=salt,
        info=b"ImAged CEK",
    )
    cek = hkdf.derive(MASTER_KEY)
    logging.debug("Derived CEK with salt %s", salt.hex())
    return cek

def derive_subkey(salt: bytes, info: bytes, length: int = 32) -> bytes:
    hkdf = HKDF(
        algorithm=hashes.SHA256(),
        length=length,
        salt=salt,
        info=info,
    )
    return hkdf.derive(MASTER_KEY)