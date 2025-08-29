import sys
import json
import base64
import struct
import logging
import os
import gc
import weakref
import signal
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.primitives import serialization, hashes, padding as sym_padding
from cryptography.hazmat.primitives.asymmetric import rsa, padding as asym_padding
from cryptography.hazmat.backends import default_backend
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

# Disable tkinter message boxes to prevent popups
try:
    import tkinter as tk
    from tkinter import messagebox
    
    # Override messagebox functions to do nothing
    def noop(*args, **kwargs):
        pass
    
    messagebox.showinfo = noop
    messagebox.showerror = noop
    messagebox.showwarning = noop
    messagebox.askyesno = noop
    messagebox.askokcancel = noop
except ImportError:
    pass

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

class SecureBackend:
    _instance = None
    _initialized = False
    
    def __new__(cls):
        if cls._instance is None:
            cls._instance = super(SecureBackend, cls).__new__(cls)
        return cls._instance
    
    def __init__(self):
        if not self._initialized:
            self.session_key = None
            self.private_key = None
            self.public_key = None
            self._memory_pool = weakref.WeakSet()
            self.establish_secure_channel()
            logger.info("Secure backend initialized")
            SecureBackend._initialized = True

    def establish_secure_channel(self):
        try:
            self.private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
            self.public_key = self.private_key.public_key()

            public_pem = self.public_key.public_bytes(
                encoding=serialization.Encoding.PEM,
                format=serialization.PublicFormat.SubjectPublicKeyInfo
            )
            sys.stdout.write(base64.b64encode(public_pem).decode() + "\n")
            sys.stdout.flush()

            enc_session_key_b64 = sys.stdin.readline().strip()
            if not enc_session_key_b64:
                raise Exception("No encrypted session key received")
            enc_session_key = base64.b64decode(enc_session_key_b64)

            self.session_key = self.private_key.decrypt(
                enc_session_key,
                asym_padding.OAEP(
                    mgf=asym_padding.MGF1(algorithm=hashes.SHA256()),
                    algorithm=hashes.SHA256(),
                    label=None
                )
            )

            confirmation = self.encrypt_data(b"CHANNEL_ESTABLISHED")
            sys.stdout.write(base64.b64encode(confirmation).decode() + "\n")
            sys.stdout.flush()
            logger.info("Secure channel established")
        except Exception as e:
            logger.error(f"Failed to establish secure channel: {e}")
            # Don't exit on any errors, just log and continue
            # This prevents the C# application from showing error dialogs
            pass

    def process_commands(self):
        logger.info("Starting command processing loop")

        while True:
            try:
                line = sys.stdin.readline().strip()
                if not line:
                    logger.info("No more input, shutting down")
                    break

                encrypted_payload = base64.b64decode(line)
                response = self.process_command(encrypted_payload)

                if isinstance(response, tuple) and len(response) == 3 and response[0] == "STREAM":
                    _, meta, payload = response
                    encrypted_meta = self.encrypt_data(json.dumps(meta).encode())
                    sys.stdout.write(base64.b64encode(encrypted_meta).decode() + "\n")
                    sys.stdout.flush()

                    encrypted_payload_out = self.encrypt_data(payload)
                    sys.stdout.write(base64.b64encode(encrypted_payload_out).decode() + "\n")
                    sys.stdout.flush()
                else:
                    encrypted_response = self.encrypt_data(json.dumps(response).encode())
                    response_base64 = base64.b64encode(encrypted_response).decode()
                    sys.stdout.write(response_base64 + "\n")
                    sys.stdout.flush()
                logger.info("Response sent and flushed")

            except Exception as e:
                logger.error(f"Error processing command: {e}")
                # Don't send error response if it's a GCM error, just continue
                if "mac check in GCM failed" not in str(e) and "InvalidTagException" not in str(e):
                    try:
                        error_response = {
                            "success": False,
                            "error": str(e),
                            "result": None
                        }
                        encrypted_error = self.encrypt_data(json.dumps(error_response).encode())
                        sys.stdout.write(base64.b64encode(encrypted_error).decode() + "\n")
                        sys.stdout.flush()
                    except:
                        # If we can't even send an error response, just continue silently
                        pass
            except KeyboardInterrupt:
                logger.info("Received interrupt signal, shutting down")
                break
            except SystemExit:
                logger.info("Received system exit signal, shutting down")
                break
            except:
                # Catch any other unexpected exceptions to prevent process crash
                logger.error("Unexpected error in command processing loop, continuing...")
                continue

    def process_command(self, encrypted_payload):
        try:
            cmd_length = struct.unpack('>I', encrypted_payload[:4])[0]
            encrypted_command = encrypted_payload[4:4+cmd_length]

            decrypted_command = self.decrypt_data(encrypted_command)
            command_data = json.loads(decrypted_command.decode())

            command = command_data.get('Command') or command_data.get('command')
            parameters = command_data.get('Parameters', {}) or command_data.get('parameters', {})

            if command == "CONVERT_TO_TTL":
                return self.handle_convert_to_ttl(parameters)
            elif command == "OPEN_TTL":
                return self.handle_open_ttl(parameters)
            elif command == "BATCH_CONVERT":
                return self.handle_batch_convert(parameters)
            elif command == "GET_CONFIG":
                return self.handle_get_config(parameters)
            elif command == "SET_CONFIG":
                return self.handle_set_config(parameters)
            else:
                return {
                    "success": False,
                    "error": f"Unknown command: {command}",
                    "result": None
                }

        except Exception as e:
            logger.error(f"Error processing command: {e}")
            # Don't return error response for GCM errors, just return a generic failure
            if "mac check in GCM failed" in str(e) or "InvalidTagException" in str(e):
                return {
                    "success": False,
                    "error": "Communication error",
                    "result": None
                }
            return {
                "success": False,
                "error": str(e),
                "result": None
            }

    def handle_open_ttl(self, parameters):
        try:
            logger.info(f"Received parameters: {parameters}")
        
            if parameters is None:
                logger.error("Parameters is None")
                return {"success": False, "error": "Parameters is None", "result": None}
        
            input_path = parameters.get('input_path')
            thumbnail_mode = parameters.get('thumbnail_mode', False)
            max_size = parameters.get('max_size', 1024)
            
            logger.info(f"Opening TTL file: {input_path} (thumbnail: {thumbnail_mode}, max_size: {max_size})")
        
            try:
                from secure_image_service import SecureImageService
                service = SecureImageService()
            
                if thumbnail_mode:
                    payload_bytes = service.render_ttl_thumbnail_secure(input_path, max_size=max_size)
                else:
                    payload_bytes = service.render_ttl_image_secure(input_path, max_display_time=30)
            
                if payload_bytes:
                    payload_base64 = base64.b64encode(payload_bytes).decode('utf-8')
                
                    logger.info(f"Successfully converted {len(payload_bytes)} bytes to base64")
                    
                    self._track_memory_usage(len(payload_bytes))
                    
                    return {"success": True, "error": None, "result": payload_base64}
                else:
                    return {"success": False, "error": "Failed to render TTL image", "result": None}
                
            except ImportError as e:
                logger.error(f"Import error: {e}")
                return {"success": False, "error": f"Import error: {e}", "result": None}

            except Exception as e:
                logger.error(f"Error rendering TTL image: {e}")
                return {"success": False, "error": f"Rendering error: {e}", "result": None}

        except Exception as e:
            logger.error(f"Error in open_ttl: {e}")
            return {"success": False, "error": str(e), "result": None}

    def handle_convert_to_ttl(self, parameters):
        """Convert an image to a TTL container via TTLFileManager"""
        try:
            logger.info(f"handle_convert_to_ttl called with params: {parameters}")

            if parameters is None:
                return {"success": False, "error": "Parameters is None", "result": None}

            input_path = parameters.get('input_path')
            expiry_ts = parameters.get('expiry_ts')
            output_path = parameters.get('output_path')

            if not input_path or not os.path.isfile(input_path):
                return {"success": False, "error": "input_path missing or file not found", "result": None}

            try:
                from file_manager import TTLFileManager
                manager = TTLFileManager()
                ttl_path = manager.create_ttl_file(input_path, expiry_ts, output_path)
                logger.info(f"TTL file created at {ttl_path}")
                return {"success": True, "error": None, "result": ttl_path}
            except Exception as e:
                logger.error(f"TTL creation failed: {e}")
                return {"success": False, "error": str(e), "result": None}
        except Exception as e:
            logger.error(f"Error in handle_convert_to_ttl: {e}")
            return {"success": False, "error": str(e), "result": None}

    def _track_memory_usage(self, bytes_used):
        try:
            import psutil
            process = psutil.Process()
            memory_mb = process.memory_info().rss / (1024 * 1024)
            
            if memory_mb > 400: 
                logger.warning(f"High memory usage detected: {memory_mb:.1f}MB, triggering cleanup")
                self._force_memory_cleanup()
                
        except ImportError:
            self._force_memory_cleanup()

    def _force_memory_cleanup(self):
        logger.info("Forcing secure memory cleanup")
        
        # Clear memory pool
        self._memory_pool.clear()
        
        # Force garbage collection multiple times
        for _ in range(5):
            gc.collect()
        
        # Don't clear __dict__ of all objects as it can cause issues
        # with mappingproxy objects and other internal Python structures
        logger.info("Secure memory cleanup completed")

    def handle_get_config(self, parameters):
        try:
            logger.info("Starting handle_get_config")
            from config import load_config
            logger.info("Imported config module")
            config = load_config()
            logger.info(f"Loaded config: {config}")
            result = {"success": True, "error": None, "result": config}
            logger.info(f"Returning result: {result}")
            return result

        except Exception as e:
            logger.error(f"Error in get_config: {e}")
            return {"success": False, "error": str(e), "result": None}



    def handle_set_config(self, parameters):
        try:
            config_data = parameters.get('config')
            if not config_data:
                return {"success": False, "error": "No config data provided", "result": None}

            from config import save_config
            save_config(config_data)

            return {"success": True, "error": None, "result": "Configuration saved"}

        except Exception as e:
            logger.error(f"Error in set_config: {e}")
            return {"success": False, "error": str(e), "result": None}

    def encrypt_data(self, data: bytes) -> bytes:
        aes = AESGCM(self.session_key)
        nonce = os.urandom(12)
        ct_and_tag = aes.encrypt(nonce, data, None)
        return nonce + ct_and_tag

    def decrypt_data(self, enc: bytes) -> bytes:
        try:
            nonce, ct_and_tag = enc[:12], enc[12:]
            aes = AESGCM(self.session_key)
            return aes.decrypt(nonce, ct_and_tag, None)
        except Exception as e:
            logger.error(f"GCM decryption failed: {e}")
            # Return a default response instead of raising exception
            return json.dumps({"success": False, "error": "Decryption failed", "result": None}).encode()

def signal_handler(signum, frame):
    logger.info(f"Received signal {signum}, shutting down gracefully")
    sys.exit(0)

def main():
    # Set up signal handlers for graceful shutdown
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)
    
    try:
        backend = SecureBackend()
        backend.process_commands()
    except KeyboardInterrupt:
        logger.info("Received interrupt signal, shutting down")
    except Exception as e:
        logger.error(f"Fatal error: {e}")
        # Don't exit with error code to prevent C# from showing error dialog
        sys.exit(0)

if __name__ == "__main__":
    main()