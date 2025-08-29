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

        public bool IsHealthy => _isInitialized && _pythonProcess != null && !_pythonProcess.HasExited;

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                // In published version, Python files are copied to the output directory
                var pythonScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pysrc", "secure_backend.py");
                
                // Check if the file exists, if not, try the development path
                if (!File.Exists(pythonScriptPath))
                {
                    var projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\.."));
                    pythonScriptPath = Path.Combine(projectDir, "ImAged", "pysrc", "secure_backend.py");
                }

                // Check if Python is available
                var pythonCommand = "python";
                try
                {
                    var pythonCheck = Process.Start(new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    pythonCheck.WaitForExit(5000);
                    if (pythonCheck.ExitCode != 0)
                    {
                        pythonCommand = "python3";
                    }
                }
                catch
                {
                    pythonCommand = "python3";
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonCommand,
                    Arguments = $"\"{pythonScriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(pythonScriptPath)
                };

                System.Diagnostics.Debug.WriteLine($"Starting Python process with script: {pythonScriptPath}");
                System.Diagnostics.Debug.WriteLine($"Working directory: {startInfo.WorkingDirectory}");
                
                if (!File.Exists(pythonScriptPath))
                {
                    throw new FileNotFoundException($"Python script not found: {pythonScriptPath}");
                }
                
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
                    var exitCode = _pythonProcess.ExitCode;
                    var errorOutput = _pythonProcess.StandardError?.ReadToEnd() ?? "No error output available";
                    throw new Exception($"Python process failed to start. Exit code: {exitCode}, Error: {errorOutput}");
                }

                var channelEstablished = await EstablishSecureChannelAsync();
                if (channelEstablished)
                {
                    _isInitialized = true;
                    System.Diagnostics.Debug.WriteLine("SecureProcessManager initialized successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Failed to establish secure channel, will retry on next operation");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize SecureProcessManager: {ex.Message}");
                // Don't set _isInitialized = true on failure, so we can retry
            }
        }

        private async Task<bool> EstablishSecureChannelAsync()
        {
            _sessionKey = GenerateSecureRandomKey(32);
            System.Diagnostics.Debug.WriteLine($"Generated session key: {_sessionKey.Length} bytes");

            var publicPemBase64 = await ReadBase64LineAsync(_outputStream, 10000);
            if (string.IsNullOrEmpty(publicPemBase64))
            {
                // Gracefully handle timeout instead of throwing exception
                System.Diagnostics.Debug.WriteLine("Timeout waiting for RSA public key from Python backend");
                return false;
            }
            var publicPemBytes = Convert.FromBase64String(publicPemBase64);
            var publicPem = Encoding.ASCII.GetString(publicPemBytes);

            var rsaPublicKey = LoadRsaPublicKeyFromPem(publicPem);
            var encryptedSessionKey = RsaOaepSha256Encrypt(rsaPublicKey, _sessionKey);
            await _inputStream.WriteLineAsync(Convert.ToBase64String(encryptedSessionKey));

            var confirmationLine = await ReadBase64LineAsync(_outputStream, 10000);
            if (string.IsNullOrEmpty(confirmationLine))
            {
                // Gracefully handle timeout instead of throwing exception
                System.Diagnostics.Debug.WriteLine("Timeout waiting for confirmation from Python backend");
                return false;
            }
            var confirmationData = Convert.FromBase64String(confirmationLine);
            var decrypted = DecryptData(confirmationData);

            if (decrypted == null)
            {
                System.Diagnostics.Debug.WriteLine("Failed to decrypt confirmation from Python backend");
                return false;
            }

            var message = Encoding.UTF8.GetString(decrypted);
            if (message != "CHANNEL_ESTABLISHED")
            {
                // Gracefully handle invalid confirmation instead of throwing exception
                System.Diagnostics.Debug.WriteLine("Invalid channel confirmation received");
                return false;
            }

            System.Diagnostics.Debug.WriteLine("Secure channel established successfully");
            return true;
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
            // Check if we're being disposed
            if (_disposed)
            {
                return new SecureResponse { Success = false, Error = "SecureProcessManager is being disposed", Result = null };
            }

            // Try to initialize if not already initialized
            if (!_isInitialized)
            {
                await InitializeAsync();
                if (!_isInitialized)
                {
                    return new SecureResponse { Success = false, Error = "Failed to initialize secure backend", Result = null };
                }
            }

            // Check if Python process is still alive
            if (_pythonProcess?.HasExited == true)
            {
                System.Diagnostics.Debug.WriteLine("Python process has exited, attempting to reinitialize");
                _isInitialized = false;
                await InitializeAsync();
                if (!_isInitialized)
                {
                    return new SecureResponse { Success = false, Error = "Failed to reinitialize secure backend", Result = null };
                }
            }

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
                    // Gracefully handle timeout instead of throwing exception
                    System.Diagnostics.Debug.WriteLine("Timeout waiting for response from Python backend");
                    return new SecureResponse { Success = false, Error = "Timeout waiting for response", Result = null };
                }

                System.Diagnostics.Debug.WriteLine($"Received response: {response.Length} chars");

                var responseData = Convert.FromBase64String(response);
                var decryptedResponse = DecryptData(responseData);

                if (decryptedResponse == null)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to decrypt response from Python backend");
                    return new SecureResponse { Success = false, Error = "Failed to decrypt response", Result = null };
                }

                var responseJson = Encoding.UTF8.GetString(decryptedResponse);

                System.Diagnostics.Debug.WriteLine($"Response JSON: Success");

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
            try
            {
                if (encryptedData == null || encryptedData.Length < 12 + 16)
                {
                    System.Diagnostics.Debug.WriteLine("Encrypted data too short for GCM decryption");
                    return null;
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
            catch (Exception ex)
            {
                // Gracefully handle GCM decryption errors instead of throwing exceptions
                System.Diagnostics.Debug.WriteLine($"GCM decryption failed: {ex.Message}");
                return null;
            }
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
                    // Return null instead of throwing exception for graceful handling
                    return null;
                }

                var readTask = reader.ReadLineAsync();
                var completed = await Task.WhenAny(readTask, Task.Delay(remaining));
                if (completed != readTask)
                {
                    // Return null instead of throwing exception for graceful handling
                    return null;
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
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_pythonProcess != null)
                {
                    if (!_pythonProcess.HasExited)
                    {
                        try
                        {
                            _pythonProcess.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                            // Process already exited, ignore.
                        }
                        catch (Exception ex)
                        {
                            // Log but ignore any other errors.
                            System.Diagnostics.Debug.WriteLine($"Error killing Python process: {ex.Message}");
                        }
                    }
                    _pythonProcess.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Log but ignore errors during disposing.
                System.Diagnostics.Debug.WriteLine($"Error during SecureProcessManager.Dispose: {ex.Message}");
            }

            try { _inputStream?.Dispose(); } catch { }
            try { _outputStream?.Dispose(); } catch { }
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
                    return ConvertBytesToBitmapSourceOptimized(imageBytes, maxSize);
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
                return ConvertBytesToBitmapSourceOptimized(imageBytes, maxSize);
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

        private BitmapSource ConvertBytesToBitmapSourceOptimized(byte[] imageBytes, int maxSize = 256)
        {
            // Validate input data
            if (imageBytes == null || imageBytes.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("ConvertBytesToBitmapSourceOptimized: Invalid image data (null or empty)");
                return null;
            }

            // Check if the data looks like a valid image (minimum size for any image format)
            if (imageBytes.Length < 100)
            {
                System.Diagnostics.Debug.WriteLine($"ConvertBytesToBitmapSourceOptimized: Image data too small ({imageBytes.Length} bytes)");
                return null;
            }

            try
            {
                using (var memoryStream = new MemoryStream(imageBytes))
                {
                    // Validate the stream can be read
                    if (!memoryStream.CanRead)
                    {
                        System.Diagnostics.Debug.WriteLine("ConvertBytesToBitmapSourceOptimized: Memory stream not readable");
                        return null;
                    }

                    using (var originalBitmap = new System.Drawing.Bitmap(memoryStream))
                    {
                        // Validate the bitmap was created successfully
                        if (originalBitmap.Width <= 0 || originalBitmap.Height <= 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"ConvertBytesToBitmapSourceOptimized: Invalid bitmap dimensions {originalBitmap.Width}x{originalBitmap.Height}");
                            return null;
                        }

                        // Calculate dimensions preserving aspect ratio
                        int width, height;
                        if (originalBitmap.Width > originalBitmap.Height)
                        {
                            width = maxSize;
                            height = (int)(originalBitmap.Height * (maxSize / (double)originalBitmap.Width));
                        }
                        else
                        {
                            height = maxSize;
                            width = (int)(originalBitmap.Width * (maxSize / (double)originalBitmap.Height));
                        }

                        // Ensure minimum size for quality
                        if (width < 64) width = 64;
                        if (height < 64) height = 64;

                        using (var thumbBitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        using (var g = System.Drawing.Graphics.FromImage(thumbBitmap))
                        {
                            // Set high-quality rendering options
                            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                            // Clear with transparent background instead of black
                            g.Clear(System.Drawing.Color.Transparent);

                            // Draw the image with high quality
                            g.DrawImage(originalBitmap, 0, 0, width, height);

                            var writeableBitmap = new WriteableBitmap(
                                width,
                                height,
                                96, 96,
                                PixelFormats.Bgra32,
                                null);

                            writeableBitmap.Lock();

                            var bitmapData = thumbBitmap.LockBits(
                                new System.Drawing.Rectangle(0, 0, width, height),
                                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                            try
                            {
                                writeableBitmap.WritePixels(
                                    new Int32Rect(0, 0, width, height),
                                    bitmapData.Scan0,
                                    width * height * 4,
                                    bitmapData.Stride);
                            }
                            finally
                            {
                                thumbBitmap.UnlockBits(bitmapData);
                                writeableBitmap.Unlock();
                            }

                            writeableBitmap.Freeze();

                            // Validate the final bitmap
                            if (writeableBitmap.PixelWidth <= 0 || writeableBitmap.PixelHeight <= 0)
                            {
                                System.Diagnostics.Debug.WriteLine("ConvertBytesToBitmapSourceOptimized: Final WriteableBitmap has invalid dimensions");
                                return null;
                            }

                            // Securely clear the input bytes
                            Array.Clear(imageBytes, 0, imageBytes.Length);

                            return writeableBitmap;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in optimized conversion: {ex.Message}");
                // Try fallback method
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
            // Validate input data
            if (imageBytes == null || imageBytes.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("ConvertBytesToBitmapSourceGdi: Invalid image data (null or empty)");
                return null;
            }

            try
            {
                using (var memoryStream = new MemoryStream(imageBytes))
                {
                    // Validate the stream can be read
                    if (!memoryStream.CanRead)
                    {
                        System.Diagnostics.Debug.WriteLine("ConvertBytesToBitmapSourceGdi: Memory stream not readable");
                        return null;
                    }

                    using (var bitmap = new System.Drawing.Bitmap(memoryStream))
                    {
                        // Validate the bitmap was created successfully
                        if (bitmap.Width <= 0 || bitmap.Height <= 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"ConvertBytesToBitmapSourceGdi: Invalid bitmap dimensions {bitmap.Width}x{bitmap.Height}");
                            return null;
                        }

                        IntPtr hBitmap = bitmap.GetHbitmap();
                        try
                        {
                            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap,
                                IntPtr.Zero,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());

                            // Validate the created BitmapSource
                            if (source == null || source.PixelWidth <= 0 || source.PixelHeight <= 0)
                            {
                                System.Diagnostics.Debug.WriteLine("ConvertBytesToBitmapSourceGdi: Failed to create valid BitmapSource");
                                return null;
                            }

                            // Freeze the BitmapSource to make it cross-thread accessible and improve performance
                            source.Freeze();

                            // Clear the original bytes to help with memory cleanup
                            Array.Clear(imageBytes, 0, imageBytes.Length);

                            return source;
                        }
                        finally
                        {
                            // Always delete the GDI handle to prevent memory leaks
                            DeleteObject(hBitmap);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GDI conversion: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Forces cleanup of any remaining GDI handles and memory
        /// </summary>
        public void ForceMemoryCleanup()
        {
            try
            {
                // Force multiple garbage collection passes to clean up any remaining BitmapSource objects
                for (int i = 0; i < 3; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                GC.Collect();

                // Also try to clean up any remaining GDI handles
                try
                {
                    // Force cleanup of any remaining WriteableBitmap objects
                    GC.AddMemoryPressure(1024 * 1024); // Add pressure to force cleanup
                    GC.Collect();
                    GC.RemoveMemoryPressure(1024 * 1024);
                }
                catch { /* Ignore errors during cleanup */ }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during memory cleanup: {ex.Message}");
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