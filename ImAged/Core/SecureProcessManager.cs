using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Asn1.X509;

namespace ImAged.Services
{
    public class SecureProcessManager : IDisposable
    {
        private Process _pythonProcess;
        private StreamWriter _inputStream;
        private StreamReader _outputStream;
        private byte[] _sessionKey;
        private bool _isInitialized = false;
        private bool _disposed = false;

        public byte[] SessionKey => _sessionKey;

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            // Get the correct path to the Python script
            var projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\.."));
            var pythonScriptPath = Path.Combine(projectDir, "ImAged", "pysrc", "secure_backend.py");

            // Start Python process
            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{pythonScriptPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = projectDir
            };

            _pythonProcess = Process.Start(startInfo);
            _inputStream = _pythonProcess.StandardInput;
            _outputStream = _pythonProcess.StandardOutput;

            // Add error monitoring
            _pythonProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"Python Error: {e.Data}");
                }
            };
            _pythonProcess.BeginErrorReadLine();

            // Check if process started successfully
            if (_pythonProcess.HasExited)
            {
                throw new Exception("Python process failed to start");
            }

			// Establish secure channel
			await EstablishSecureChannelAsync();
            _isInitialized = true;
        }

		private async Task EstablishSecureChannelAsync()
		{
			// Generate session key
			_sessionKey = GenerateSecureRandomKey(32);
			System.Diagnostics.Debug.WriteLine($"Generated session key: {_sessionKey.Length} bytes");

			// 1) Read Python's RSA public key (PEM) sent as Base64(Pem)
			var publicPemBase64 = await ReadBase64LineAsync(_outputStream, 10000);
			if (string.IsNullOrEmpty(publicPemBase64))
			{
				throw new SecurityException("No RSA public key received from Python backend");
			}
			var publicPemBytes = Convert.FromBase64String(publicPemBase64);
			var publicPem = Encoding.ASCII.GetString(publicPemBytes);

			// 2) Encrypt session key with RSA-OAEP(SHA-256) and send Base64(cipher)
			var rsaPublicKey = LoadRsaPublicKeyFromPem(publicPem);
			var encryptedSessionKey = RsaOaepSha256Encrypt(rsaPublicKey, _sessionKey);
			await _inputStream.WriteLineAsync(Convert.ToBase64String(encryptedSessionKey));

			// 3) Read confirmation: AES-GCM(nonce+cipher+tag) over "CHANNEL_ESTABLISHED"
			var confirmationLine = await ReadBase64LineAsync(_outputStream, 10000);
			if (string.IsNullOrEmpty(confirmationLine))
			{
				throw new SecurityException("No confirmation received from Python backend");
			}
			var confirmationData = Convert.FromBase64String(confirmationLine);
			var decrypted = DecryptData(confirmationData);
			var message = Encoding.UTF8.GetString(decrypted);
			if (message != "CHANNEL_ESTABLISHED")
			{
				throw new SecurityException("Invalid channel confirmation");
			}

			System.Diagnostics.Debug.WriteLine("Secure channel established successfully");
		}

        private byte[] GenerateSecureRandomKey(int length)
        {
            var key = new byte[length];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(key);
            }
            return key;
        }

		private byte[] CreatePayload(byte[] encryptedCommand)
		{
			// Build: 4-byte big-endian length + encrypted command
			var lengthBytes = BitConverter.GetBytes(encryptedCommand.Length);
			if (BitConverter.IsLittleEndian)
			{
				Array.Reverse(lengthBytes);
			}
			var payload = new byte[4 + encryptedCommand.Length];
			Array.Copy(lengthBytes, 0, payload, 0, 4);
			Array.Copy(encryptedCommand, 0, payload, 4, encryptedCommand.Length);
			return payload;
		}

		private AsymmetricKeyParameter LoadRsaPublicKeyFromPem(string pem)
		{
			AsymmetricKeyParameter keyParams = null;
			var stringReader = new StringReader(pem);
			var pemReader = new PemReader(stringReader);
			object pemObject = pemReader.ReadObject();
			keyParams = pemObject as AsymmetricKeyParameter;
			if (keyParams == null)
			{
				var spki = pemObject as SubjectPublicKeyInfo;
				if (spki != null)
				{
					keyParams = PublicKeyFactory.CreateKey(spki);
				}
			}
			if (keyParams == null)
			{
				throw new SecurityException("Failed to parse RSA public key from PEM.");
			}
			return keyParams;
		}

		private byte[] RsaOaepSha256Encrypt(AsymmetricKeyParameter publicKey, byte[] data)
		{
			IAsymmetricBlockCipher engine = new OaepEncoding(new RsaEngine(), new Sha256Digest(), new Sha256Digest(), null);
			engine.Init(true, publicKey);
			return engine.ProcessBlock(data, 0, data.Length);
		}

		public async Task<SecureResponse> SendCommandAsync(SecureCommand command)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("SecureProcessManager not initialized");

            // Serialize and encrypt command
			var commandJson = System.Text.Json.JsonSerializer.Serialize(command);
			var commandBytes = Encoding.UTF8.GetBytes(commandJson);
			var encryptedCommand = EncryptData(commandBytes);

			// Create payload (big-endian length + encrypted)
			var payload = CreatePayload(encryptedCommand);

            // Send command
            await _inputStream.WriteLineAsync(Convert.ToBase64String(payload));

            // Read response with timeout
            System.Diagnostics.Debug.WriteLine("Waiting for response from Python...");

            // Use a timeout to prevent hanging
            var response = await ReadBase64LineAsync(_outputStream, 5000);

            if (string.IsNullOrEmpty(response))
            {
                throw new Exception("No response received from Python backend");
            }

            System.Diagnostics.Debug.WriteLine($"Received response: {response.Length} chars");

			var responseData = Convert.FromBase64String(response);
			var decryptedResponse = DecryptData(responseData);
			var responseJson = Encoding.UTF8.GetString(decryptedResponse);

            System.Diagnostics.Debug.WriteLine($"Response JSON: {responseJson}");

            return System.Text.Json.JsonSerializer.Deserialize<SecureResponse>(responseJson);
        }

		private byte[] EncryptData(byte[] data)
		{
			// AES-GCM with 12-byte nonce, output: nonce + ciphertext||tag (16 bytes tag)
			var random = new SecureRandom();
			var nonce = new byte[12];
			random.NextBytes(nonce);

			var cipher = new GcmBlockCipher(new AesFastEngine());
			var parameters = new AeadParameters(new KeyParameter(_sessionKey), 128, nonce, null);
			cipher.Init(true, parameters);

			var output = new byte[cipher.GetOutputSize(data.Length)];
			int len = cipher.ProcessBytes(data, 0, data.Length, output, 0);
			len += cipher.DoFinal(output, len);

			var result = new byte[nonce.Length + len];
			Array.Copy(nonce, 0, result, 0, nonce.Length);
			Array.Copy(output, 0, result, nonce.Length, len);
			return result;
		}

		private byte[] DecryptData(byte[] encryptedData)
		{
			// AES-GCM with 12-byte nonce prefix
			if (encryptedData == null || encryptedData.Length < 12 + 16)
			{
				throw new SecurityException("Encrypted data too short.");
			}
			var nonce = new byte[12];
			Array.Copy(encryptedData, 0, nonce, 0, 12);
			var cipherTextAndTag = new byte[encryptedData.Length - 12];
			Array.Copy(encryptedData, 12, cipherTextAndTag, 0, cipherTextAndTag.Length);

			var cipher = new GcmBlockCipher(new AesFastEngine());
			var parameters = new AeadParameters(new KeyParameter(_sessionKey), 128, nonce, null);
			cipher.Init(false, parameters);

			var output = new byte[cipher.GetOutputSize(cipherTextAndTag.Length)];
			int len = cipher.ProcessBytes(cipherTextAndTag, 0, cipherTextAndTag.Length, output, 0);
			len += cipher.DoFinal(output, len);

			var plaintext = new byte[len];
			Array.Copy(output, 0, plaintext, 0, len);
			return plaintext;
		}

        private static bool IsValidBase64(string s)
		{
			if (string.IsNullOrEmpty(s) || (s.Length % 4) != 0) return false;
			for (int i = 0; i < s.Length; i++)
			{
				char c = s[i];
				bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
				          (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '=';
				if (!ok) return false;
			}
			try
			{
				Convert.FromBase64String(s);
				return true;
			}
			catch
			{
				return false;
			}
		}

		private async Task<string> ReadBase64LineAsync(StreamReader reader, int timeoutMs)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (DateTime.UtcNow < deadline)
			{
				var readTask = reader.ReadLineAsync();
				var completed = await Task.WhenAny(readTask, Task.Delay(250));
				if (completed == readTask)
				{
					var line = await readTask;
					if (string.IsNullOrEmpty(line)) continue;
					if (IsValidBase64(line)) return line;
					// Skip non-base64 noise (e.g., accidental prints)
					continue;
				}
			}
			throw new TimeoutException("Timeout waiting for valid Base64 line.");
		}

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    // Check if process is still running before trying to kill it
                    if (_pythonProcess != null && !_pythonProcess.HasExited)
                    {
                        _pythonProcess.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process has already exited, ignore the exception
                }
                catch (Exception ex)
                {
                    // Log other exceptions if needed
                    System.Diagnostics.Debug.WriteLine($"Error killing Python process: {ex.Message}");
                }
                finally
                {
                    _pythonProcess?.Dispose();
                    _inputStream?.Dispose();
                    _outputStream?.Dispose();
                    _disposed = true;
                }
            }
        }
    }

    public class SecureCommand
    {
        public string Command { get; set; }
        public Dictionary<string, object> Parameters { get; set; }

        public SecureCommand(string command, Dictionary<string, object> parameters = null)
        {
            Command = command;
            Parameters = parameters ?? new Dictionary<string, object>();
        }
    }

    public class SecureResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public bool Success { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("result")]
        public object Result { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public string Error { get; set; }
    }
}