import time
import struct
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from cryptography.hazmat.backends import default_backend
import os
import hmac
from typing import Tuple


class InvalidInputException(Exception):
    def __init__(self, msg):
        self.msg = msg
    def __str__(self):
        return str(self.msg)


class InvalidTagException(Exception):
    def __str__(self):
        return 'The authentication tag is invalid.'


class AES_GCM:
    def __init__(self, key: bytes):
        self._perf_data = {
            'total_encrypt': 0,
            'total_decrypt': 0,
            'aes_operations': 0,
            'ghash_operations': 0
        }
        
        if len(key) not in (16, 24, 32):
            raise InvalidInputException('Key must be 16, 24, or 32 bytes long.')
        
        start_time = time.perf_counter()
        
        self._key = key
        self._backend = default_backend()
        self._seen_nonces = set()
        self._enforce_iv_uniqueness = True
        self._invocations_96 = 0
        self._invocations_non96 = 0
        
        self._aes_ecb_cipher = Cipher(
            algorithms.AES(self._key),
            modes.ECB(),
            backend=self._backend
        )
        self._auth_key = self._compute_auth_key()
        
        # Lazy table computation - only build when needed
        self._pre_table = None
        self._table_built = False
        
        init_time = time.perf_counter() - start_time
        self._perf_data['init_time'] = init_time

    def _compute_auth_key(self) -> int:
        # NIST SP 800-38D: H = E(K, 0^128)
        start = time.perf_counter()
        encryptor = self._aes_ecb_cipher.encryptor()
        result = int.from_bytes(encryptor.update(b'\x00' * 16) + encryptor.finalize(), 'big')
        self._perf_data['aes_operations'] += 1
        self._perf_data['auth_key_time'] = time.perf_counter() - start
        return result

    def _ensure_table_built(self):
        """Lazily build the GHASH table only when needed"""
        if not self._table_built:
            H = self._auth_key
            table = []
            for i in range(16):
                row = []
                shift = 8 * i
                for b in range(256):
                    row.append(self._gf_2_128_mul_fast(H, b << shift))
                table.append(tuple(row))
            self._pre_table = tuple(table)
            self._table_built = True

    def _precompute_ghash_table(self):
        # V2's optimized table precomputation for fast GHASH
        self._ensure_table_built()
        return self._pre_table

    def _mul_H(self, x: int) -> int:
        # V2's optimized multiplication using precomputed table
        acc = 0
        for i in range(16):
            byte = (x >> (8 * i)) & 0xFF
            acc ^= self._pre_table[i][byte]
        return acc

    @staticmethod
    def _gf_2_128_mul(x: int, y: int) -> int:
        # V2's optimized GF(2^128) multiplication
        res = 0
        for i in range(127, -1, -1):
            res ^= x * ((y >> i) & 1)
            x = (x >> 1) ^ ((x & 1) * 0xE1000000000000000000000000000000)
        return res

    def _gf_2_128_mul_fast(self, x: int, y: int) -> int:
        # V1's fast multiplication - better for small operations
        # NIST SP 800-38D Algorithm 1: MSB-first bit processing
        # R = 11100001 || 0^120 = 0xE1 followed by 15 zero bytes
        R = 0xE1000000000000000000000000000000
        
        Z = 0
        V = y & 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF
        
        for i in range(128):
            bit_pos = 127 - i
            x_i = (x >> bit_pos) & 1
            
            if x_i:
                Z ^= V
                
            if V & 1:
                V = (V >> 1) ^ R
            else:
                V = V >> 1
        
        return Z & 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF

    def _ghash_optimized(self, aad: bytes, ciphertext: bytes) -> int:
        # Smart GHASH: use simple approach for small data, optimized for large data
        total_len = len(aad) + len(ciphertext)
        
        # For small data (< 1KB), use simple multiplication to avoid table overhead
        if total_len < 1024:
            return self._ghash_simple(aad, ciphertext)
        
        # For larger data, use optimized table-based approach
        self._ensure_table_built()
        return self._ghash_table_based(aad, ciphertext)

    def _ghash_simple(self, aad: bytes, ciphertext: bytes) -> int:
        """Simple GHASH implementation for small data"""
        tag = 0
        aad_len = len(aad)
        c_len = len(ciphertext)

        # Process AAD in 16-byte blocks
        full = (aad_len // 16) * 16
        for i in range(0, full, 16):
            tag = self._gf_2_128_mul_fast(tag ^ int.from_bytes(aad[i:i+16], 'big'), self._auth_key)
        if aad_len != full:
            last = aad[full:] + b'\x00' * (16 - (aad_len - full))
            tag = self._gf_2_128_mul_fast(tag ^ int.from_bytes(last, 'big'), self._auth_key)

        # Process ciphertext in 16-byte blocks
        full = (c_len // 16) * 16
        for i in range(0, full, 16):
            tag = self._gf_2_128_mul_fast(tag ^ int.from_bytes(ciphertext[i:i+16], 'big'), self._auth_key)
        if c_len != full:
            last = ciphertext[full:] + b'\x00' * (16 - (c_len - full))
            tag = self._gf_2_128_mul_fast(tag ^ int.from_bytes(last, 'big'), self._auth_key)

        # Add length block
        len_block = ((aad_len * 8) << 64) | (c_len * 8)
        return self._gf_2_128_mul_fast(tag ^ len_block, self._auth_key)

    def _ghash_table_based(self, aad: bytes, ciphertext: bytes) -> int:
        """Table-based GHASH implementation for large data"""
        tag = 0
        aad_len = len(aad)
        c_len = len(ciphertext)

        # Process AAD in 16-byte blocks using table-based multiplication
        full = (aad_len // 16) * 16
        for i in range(0, full, 16):
            tag = self._mul_H(tag ^ int.from_bytes(aad[i:i+16], 'big'))
        if aad_len != full:
            last = aad[full:] + b'\x00' * (16 - (aad_len - full))
            tag = self._mul_H(tag ^ int.from_bytes(last, 'big'))

        # Process ciphertext in 16-byte blocks using table-based multiplication
        full = (c_len // 16) * 16
        for i in range(0, full, 16):
            tag = self._mul_H(tag ^ int.from_bytes(ciphertext[i:i+16], 'big'))
        if c_len != full:
            last = ciphertext[full:] + b'\x00' * (16 - (c_len - full))
            tag = self._mul_H(tag ^ int.from_bytes(last, 'big'))

        # Add length block
        len_block = ((aad_len * 8) << 64) | (c_len * 8)
        return self._mul_H(tag ^ len_block)

    def _ghash(self, aad: bytes, ciphertext: bytes) -> int:
        # Use the optimized implementation
        return self._ghash_optimized(aad, ciphertext)

    def encrypt(self, nonce: bytes, plaintext: bytes, associated_data: bytes = b'', tag_len_bytes: int = 16) -> bytes:
        start_time = time.perf_counter()
        
        if len(nonce) == 0:
            raise InvalidInputException('Nonce (IV) must not be empty.')
        if tag_len_bytes not in (16, 15, 14, 13, 12, 8, 4):
            raise InvalidInputException('tag_len_bytes must be one of {16,15,14,13,12,8,4}.')

        # V1's IV uniqueness enforcement
        if self._enforce_iv_uniqueness:
            if nonce in self._seen_nonces:
                raise InvalidInputException('IV/nonce reuse detected for this key. Each IV must be unique.')
            self._seen_nonces.add(nonce)

        if len(nonce) == 12:
            self._invocations_96 += 1
            if self._invocations_96 >= 2**32:
                raise InvalidInputException('Invocation limit exceeded for 96-bit IVs with this key.')
        else:
            self._invocations_non96 += 1
            if self._invocations_non96 >= 2**32:
                raise InvalidInputException('Invocation limit exceeded for non-96-bit IVs with this key.')

        # V1's J0 derivation (handles both 96-bit and arbitrary-length IVs)
        j0 = self._derive_J0(nonce)
        
        # Use cryptography library for CTR mode (heavy AES operations)
        cipher = Cipher(
            algorithms.AES(self._key),
            modes.CTR(self._inc32(j0)),
            backend=self._backend
        )
        encryptor = cipher.encryptor()
        ciphertext = encryptor.update(plaintext) + encryptor.finalize()
        self._perf_data['aes_operations'] += 1

        # Our own optimized GHASH implementation
        tag = self._ghash_optimized(associated_data, ciphertext)
        
        # NIST SP 800-38D: T = GHASH ⊕ E_K(J0)
        # Use cryptography library for single AES block encryption
        encryptor = self._aes_ecb_cipher.encryptor()
        tag_mask = int.from_bytes(encryptor.update(j0) + encryptor.finalize(), 'big')
        tag ^= tag_mask
        self._perf_data['aes_operations'] += 1

        total_time = time.perf_counter() - start_time
        self._perf_data['total_encrypt'] += total_time
        
        # V1's tag truncation support
        full_tag = tag.to_bytes(16, 'big')
        return ciphertext + full_tag[:tag_len_bytes]



    def decrypt(self, nonce: bytes, data: bytes, associated_data: bytes = b'', tag_len_bytes: int = 16) -> bytes:
        start_time = time.perf_counter()
        
        if len(nonce) == 0:
            raise InvalidInputException('Nonce (IV) must not be empty.')
        if tag_len_bytes not in (16, 15, 14, 13, 12, 8, 4):
            raise InvalidInputException('tag_len_bytes must be one of {16,15,14,13,12,8,4}.')
        if len(data) < tag_len_bytes:
            raise InvalidInputException('Data too short to contain tag.')

        ciphertext = data[:-tag_len_bytes]
        received_tag = data[-tag_len_bytes:]
        
        # V1's J0 derivation
        j0 = self._derive_J0(nonce)

        # Our own optimized GHASH implementation
        computed_tag_val = self._ghash_optimized(associated_data, ciphertext)
        
        # NIST SP 800-38D: T = GHASH ⊕ E_K(J0)
        # Use cryptography library for single AES block encryption
        encryptor = self._aes_ecb_cipher.encryptor()
        tag_mask = int.from_bytes(encryptor.update(j0) + encryptor.finalize(), 'big')
        computed_tag_val ^= tag_mask
        computed_tag_full = computed_tag_val.to_bytes(16, 'big')
        self._perf_data['aes_operations'] += 1

        # V1's tag verification with truncation
        msb_trunc = computed_tag_full[:tag_len_bytes]
        if not hmac.compare_digest(msb_trunc, received_tag):
            raise InvalidTagException()

        # Use cryptography library for CTR mode (heavy AES operations)
        cipher = Cipher(
            algorithms.AES(self._key),
            modes.CTR(self._inc32(j0)),
            backend=self._backend
        )
        decryptor = cipher.decryptor()
        plaintext = decryptor.update(ciphertext) + decryptor.finalize()
        self._perf_data['aes_operations'] += 1

        total_time = time.perf_counter() - start_time
        self._perf_data['total_decrypt'] += total_time
        
        return plaintext



    def set_enforce_iv_uniqueness(self, enforce: bool) -> None:
        self._enforce_iv_uniqueness = bool(enforce)

    def reset_iv_registry(self) -> None:
        self._seen_nonces.clear()

    def _inc32(self, block16: bytes) -> bytes:
        # V1's counter increment (preserves correctness)
        if len(block16) != 16:
            raise InvalidInputException('Counter block must be 16 bytes.')
        prefix = block16[:12]
        ctr = int.from_bytes(block16[12:], 'big')
        ctr = (ctr + 1) & 0xFFFFFFFF
        return prefix + ctr.to_bytes(4, 'big')

    def _derive_J0(self, iv: bytes) -> bytes:
        # V1's J0 derivation (handles both 96-bit and arbitrary-length IVs correctly)
        if len(iv) == 12:
            # 96-bit IV: J0 = IV || 0^31 || 1
            return iv + b'\x00\x00\x00\x01'
        else:
            if len(iv) < 16:
                iv_len_bits = len(iv) * 8
                s = (16 - (len(iv) % 16)) % 16
                ghash_input = iv + (b'\x00' * s) + struct.pack('>Q', iv_len_bits)
            else:
                # Standard NIST: J0 = GHASH_H(IV || 0^s || [len(IV)]_64)
                # where s satisfies len(IV)*8 + s + 64 ≡ 0 (mod 128)
                iv_len_bits = len(iv) * 8
                s_bits = (128 - ((iv_len_bits + 64) % 128)) % 128
                s_bytes = s_bits // 8
                ghash_input = iv + (b'\x00' * s_bytes) + struct.pack('>Q', iv_len_bits)
            
            # Use optimized GHASH for J0 computation
            hash_val = 0
            for i in range(0, len(ghash_input), 16):
                block = int.from_bytes(ghash_input[i:i+16], 'big')
                hash_val = self._gf_2_128_mul_fast(hash_val ^ block, self._auth_key)
            return hash_val.to_bytes(16, 'big')

    def debug_vector(self, nonce: bytes, plaintext: bytes, associated_data: bytes = b'', tag_len_bytes: int = 16) -> dict:
        # V1's debug function (preserved for validation)
        if tag_len_bytes not in (16, 15, 14, 13, 12, 8, 4):
            raise InvalidInputException("tag_len_bytes must be one of {16,15,14,13,12,8,4}.")

        info = {}
        info['H'] = self._auth_key.to_bytes(16, 'big').hex()

        J0 = self._derive_J0(nonce)
        info['J0'] = J0.hex()

        # Use V2's bulk CTR for ciphertext generation
        cipher = Cipher(
            algorithms.AES(self._key),
            modes.CTR(self._inc32(J0)),
            backend=self._backend
        )
        encryptor = cipher.encryptor()
        ct = encryptor.update(plaintext) + encryptor.finalize()
        info['Ciphertext'] = ct.hex()

        # Use V2's optimized GHASH
        g = self._ghash(associated_data, ct)
        info['GHASH'] = g.to_bytes(16, 'big').hex()

        enc = self._aes_ecb_cipher.encryptor()
        ekj0 = enc.update(J0) + enc.finalize()
        info['E_K(J0)'] = ekj0.hex()

        full_tag = (g ^ int.from_bytes(ekj0, 'big')).to_bytes(16, 'big')
        info['ComputedTag_full'] = full_tag.hex()
        info['ComputedTag_trunc'] = full_tag[:tag_len_bytes].hex()

        try:
            from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
            from cryptography.hazmat.backends import default_backend
            encryptor = Cipher(algorithms.AES(self._key), modes.GCM(nonce, min_tag_length=tag_len_bytes), backend=default_backend()).encryptor()
            if associated_data:
                encryptor.authenticate_additional_data(associated_data)
            ref_ct = encryptor.update(plaintext) + encryptor.finalize()
            info['ReferenceTag_full'] = encryptor.tag.hex()
            info['ReferenceTag_trunc'] = encryptor.tag[:tag_len_bytes].hex()
            info['ReferenceCiphertext'] = ref_ct.hex()
        except Exception as e:
            info['ReferenceError'] = str(e)

        return info

    def get_performance_stats(self) -> dict:
        return self._perf_data
