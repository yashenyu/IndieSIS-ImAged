using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Asn1.X509;
using Newtonsoft.Json;


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
        private readonly System.Threading.SemaphoreSlim _ioLock = new System.Threading.SemaphoreSlim(1, 1);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public byte[] SessionKey => _sessionKey;

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            var projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\.."));
            var pythonScriptPath = Path.Combine(projectDir, "ImAged", "pysrc", "secure_backend.py");

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

            _pythonProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"Python Error: {e.Data}");
                }
            };
            _pythonProcess.BeginErrorReadLine();

            if (_pythonProcess.HasExited)
            {
                throw new Exception("Python process failed to start");
            }

            await EstablishSecureChannelAsync();
            _isInitialized = true;
        }

        private async Task EstablishSecureChannelAsync()
        {
            _sessionKey = GenerateSecureRandomKey(32);
            System.Diagnostics.Debug.WriteLine($"Generated session key: {_sessionKey.Length} bytes");

            var publicPemBase64 = await ReadBase64LineAsync(_outputStream, 10000);
            if (string.IsNullOrEmpty(publicPemBase64))
            {
                throw new SecurityException("No RSA public key received from Python backend");
            }
            var publicPemBytes = Convert.FromBase64String(publicPemBase64);
            var publicPem = Encoding.ASCII.GetString(publicPemBytes);

            var rsaPublicKey = LoadRsaPublicKeyFromPem(publicPem);
            var encryptedSessionKey = RsaOaepSha256Encrypt(rsaPublicKey, _sessionKey);
            await _inputStream.WriteLineAsync(Convert.ToBase64String(encryptedSessionKey));

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

            await _ioLock.WaitAsync();
            try
            {
                var commandJson = JsonConvert.SerializeObject(command);
                var commandBytes = Encoding.UTF8.GetBytes(commandJson);
                var encryptedCommand = EncryptData(commandBytes);

                var payload = CreatePayload(encryptedCommand);

                await _inputStream.WriteLineAsync(Convert.ToBase64String(payload));

                System.Diagnostics.Debug.WriteLine("Waiting for response from Python...");

                var response = await ReadBase64LineAsync(_outputStream, 10000);

                if (string.IsNullOrEmpty(response))
                {
                    throw new Exception("No response received from Python backend");
                }

                System.Diagnostics.Debug.WriteLine($"Received response: {response.Length} chars");

                var responseData = Convert.FromBase64String(response);
                var decryptedResponse = DecryptData(responseData);
                var responseJson = Encoding.UTF8.GetString(decryptedResponse);

                System.Diagnostics.Debug.WriteLine($"Response JSON: {responseJson}");

                return JsonConvert.DeserializeObject<SecureResponse>(responseJson);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<string> EncryptDataAsync(string data)
        {
            var command = new SecureCommand("ENCRYPT_DATA", new Dictionary<string, object> { { "data", data } });
            var response = await SendCommandAsync(command);

            if (response.Success)
            {
                return response.Result?.ToString();
            }
            else
            {
                throw new Exception($"Encryption failed: {response.Error}");
            }
        }

        public async Task<string> DecryptDataAsync(string encryptedData)
        {
            var command = new SecureCommand("DECRYPT_DATA", new Dictionary<string, object> { { "data", encryptedData } });
            var response = await SendCommandAsync(command);

            if (response.Success)
            {
                return response.Result?.ToString();
            }
            else
            {
                throw new Exception($"Decryption failed: {response.Error}");
            }
        }

        private byte[] EncryptData(byte[] data)
        {
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
            if (encryptedData == null || encryptedData.Length < 12 + 16)
            {
                throw new SecurityException("Encrypted data too short.");
            }
            var nonce = new byte[12];
            Array.Copy(encryptedData, 0, nonce, 0, 12);
            var cipherTextAndTag = new byte[encryptedData.Length - 12];
            Array.Copy(encryptedData, 12, cipherTextAndTag, 0, cipherTextAndTag.Length);

            var cipher = new GcmBlockCipher(new AesEngine());
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
            while (true)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException("Timeout waiting for valid Base64 line.");
                }

                var readTask = reader.ReadLineAsync();
                var completed = await Task.WhenAny(readTask, Task.Delay(remaining));
                if (completed != readTask)
                {
                    throw new TimeoutException("Timeout waiting for valid Base64 line.");
                }

                var line = await readTask;
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                if (IsValidBase64(line))
                {
                    return line;
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    if (_pythonProcess != null && !_pythonProcess.HasExited)
                    {
                        _pythonProcess.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (Exception ex)
                {
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

        public async Task<string> ConvertImageToTtlAsync(string imagePath, DateTimeOffset expiryUtc)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"Image file not found: {imagePath}");

            var parameters = new Dictionary<string, object>
            {
                { "input_path", imagePath },
                { "expiry_ts", expiryUtc.ToUnixTimeSeconds() }
            };

            var command = new SecureCommand("CONVERT_TO_TTL", parameters);
            var response = await SendCommandAsync(command);

            if (response.Success)
            {
                return response.Result?.ToString();
            }
            else
            {
                throw new Exception($"TTL conversion failed: {response.Error}");
            }
        }


        public async Task<BitmapSource> OpenTtlFileAsync(string ttlPath, bool thumbnailMode = false, int maxSize = 256)
        {
            if (!File.Exists(ttlPath))
                throw new FileNotFoundException($"TTL file not found: {ttlPath}");

            var parameters = new Dictionary<string, object>
            {
                { "input_path",     ttlPath        },
                { "thumbnail_mode", thumbnailMode  }
            };

            if (thumbnailMode)
                parameters.Add("max_size", maxSize);

            foreach (var kvp in parameters)
            {
                System.Diagnostics.Debug.WriteLine($"{kvp.Key} = {kvp.Value ?? "null"}");
            }

            var command = new SecureCommand("OPEN_TTL", parameters);
            var commandJson = JsonConvert.SerializeObject(command);
            System.Diagnostics.Debug.WriteLine($"Command JSON: {commandJson}");

            var response = await SendCommandAsync(command);

            if (response.Success)
            {
                var imageBytes = Convert.FromBase64String(response.Result.ToString());

                // Use more memory-efficient conversion for thumbnails
                if (thumbnailMode)
                {
                    return ConvertBytesToBitmapSourceOptimized(imageBytes);
                }

                return ConvertBytesToBitmapSourceGdi(imageBytes);
            }
            else
            {
                throw new Exception($"TTL file opening failed: {response.Error}");
            }
        }

        public async Task<BitmapSource> OpenTtlThumbnailAsync(string ttlPath, int maxSize = 64)
        {
            if (!File.Exists(ttlPath))
                throw new FileNotFoundException($"TTL file not found: {ttlPath}");

            var parameters = new Dictionary<string, object>
            {
                { "input_path", ttlPath },
                { "thumbnail_mode", true },
                { "max_size", maxSize }
            };

            var command = new SecureCommand("OPEN_TTL", parameters);
            var response = await SendCommandAsync(command);

            if (response.Success)
            {
                var imageBytes = Convert.FromBase64String(response.Result.ToString());

                // Use memory-efficient conversion for thumbnails
                return ConvertBytesToBitmapSourceOptimized(imageBytes);
            }
            else
            {
                throw new Exception($"TTL thumbnail generation failed: {response.Error}");
            }
        }

        private async Task<byte[]> GetStreamedImageBytesAsync(string ttlPath)
        {
            var parameters = new Dictionary<string, object>
            {
                { "input_path", ttlPath }
            };

            var command = new SecureCommand("OPEN_TTL", parameters);
            var commandJson = JsonConvert.SerializeObject(command);
            var commandBytes = Encoding.UTF8.GetBytes(commandJson);
            var encryptedCommand = EncryptData(commandBytes);
            var payload = CreatePayload(encryptedCommand);

            await _inputStream.WriteLineAsync(Convert.ToBase64String(payload));

            var metadataResponse = await ReadBase64LineAsync(_outputStream, 5000);
            var metadataData = Convert.FromBase64String(metadataResponse);
            var decryptedMetadata = DecryptData(metadataData);
            var metadataJson = Encoding.UTF8.GetString(decryptedMetadata);
            var metadata = JsonConvert.DeserializeObject<StreamMetadata>(metadataJson);


            if (metadata.HasPayload)
            {
                var payloadResponse = await ReadBase64LineAsync(_outputStream, 5000);
                var payloadData = Convert.FromBase64String(payloadResponse);
                var decryptedPayload = DecryptData(payloadData);
                return decryptedPayload;
            }

            throw new Exception("No image payload received from Python backend");
        }

        private BitmapSource ConvertBytesToBitmapSourceOptimized(byte[] imageBytes)
        {
            try
            {
                using (var memoryStream = new MemoryStream(imageBytes))
                using (var bitmap = new System.Drawing.Bitmap(memoryStream))
                {
                    var writeableBitmap = new WriteableBitmap(
                        bitmap.Width,
                        bitmap.Height,
                        96, 96,
                        PixelFormats.Bgr24,
                        null);

                    writeableBitmap.Lock();

                    var bitmapData = bitmap.LockBits(
                        new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                    try
                    {
                        writeableBitmap.WritePixels(
                            new Int32Rect(0, 0, bitmap.Width, bitmap.Height),
                            bitmapData.Scan0,
                            bitmap.Width * bitmap.Height * 3,
                            bitmapData.Stride);
                    }
                    finally
                    {
                        bitmap.UnlockBits(bitmapData);
                        writeableBitmap.Unlock();
                    }

                    writeableBitmap.Freeze();

                    // Securely clear the input bytes
                    for (int i = 0; i < imageBytes.Length; i++)
                    {
                        imageBytes[i] = 0;
                    }

                    return writeableBitmap;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in optimized conversion: {ex.Message}");
                return ConvertBytesToBitmapSourceGdi(imageBytes);
            }
        }

        private BitmapSource ConvertBytesToBitmapSource(byte[] imageBytes)
        {
            var imageBytesCopy = new byte[imageBytes.Length];
            Array.Copy(imageBytes, imageBytesCopy, imageBytes.Length);

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.CreateOptions = BitmapCreateOptions.PreservePixelFormat;

            using (var memoryStream = new MemoryStream(imageBytesCopy))
            {
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
            }

            bitmapImage.Freeze();
            return bitmapImage;
        }

        private BitmapSource ConvertBytesToBitmapSourceGdi(byte[] imageBytes)
        {
            using (var memoryStream = new MemoryStream(imageBytes))
            using (var bitmap = new System.Drawing.Bitmap(memoryStream))
            {
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
        }

        private BitmapSource CreateWriteableBitmapFromBytes(byte[] imageBytes)
        {
            var imageBytesCopy = new byte[imageBytes.Length];
            Array.Copy(imageBytes, imageBytesCopy, imageBytes.Length);

            using (var memoryStream = new MemoryStream(imageBytesCopy))
            using (var bitmap = new System.Drawing.Bitmap(memoryStream))
            {
                var writeableBitmap = new WriteableBitmap(
                    bitmap.Width,
                    bitmap.Height,
                    96, 96,
                    PixelFormats.Pbgra32,
                    null);

                writeableBitmap.Lock();

                var bitmapData = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                try
                {
                    writeableBitmap.WritePixels(
                        new Int32Rect(0, 0, bitmap.Width, bitmap.Height),
                        bitmapData.Scan0,
                        bitmap.Width * bitmap.Height * 4,
                        bitmapData.Stride);
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                    writeableBitmap.Unlock();
                }

                return writeableBitmap;
            }
        }


        private BitmapSource ConvertSystemDrawingToWpf(System.Drawing.Bitmap bitmap)
        {
            var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = memoryStream;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            return bitmapImage;
        }


        public async Task<Dictionary<string, object>> GetConfigurationAsync()
        {
            var command = new SecureCommand("GET_CONFIG", new Dictionary<string, object>());
            var response = await SendCommandAsync(command);

            if (response.Success)
            {
                if (response.Result != null)
                {
                    try
                    {
                        var configJson = JsonConvert.SerializeObject(response.Result);
                        return JsonConvert.DeserializeObject<Dictionary<string, object>>(configJson);
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"JSON deserialization error: {ex.Message}");
                        return new Dictionary<string, object>();
                    }
                }
                return new Dictionary<string, object>();
            }
            else
            {
                throw new Exception($"Failed to get configuration: {response.Error}");
            }
        }

        public async Task SetConfigurationAsync(Dictionary<string, object> config)
        {
            var parameters = new Dictionary<string, object>
            {
                { "config", config }
            };

            var command = new SecureCommand("SET_CONFIG", parameters);
            var response = await SendCommandAsync(command);

            if (!response.Success)
            {
                throw new Exception($"Failed to set configuration: {response.Error}");
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
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("result")]
        public object Result { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }
    }


    public class StreamMetadata
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("result")]
        public StreamResult Result { get; set; }

        [JsonProperty("has_payload")]
        public bool HasPayload { get; set; }
    }

    public class StreamResult
    {
        [JsonProperty("mime")]
        public string Mime { get; set; }

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("fallback")]
        public bool Fallback { get; set; }
    }
}