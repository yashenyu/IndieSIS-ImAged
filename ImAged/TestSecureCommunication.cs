using System;
using System.Threading.Tasks;
using ImAged.Services;

namespace ImAged
{
    public class TestSecureCommunication
    {
        public static async Task TestBasicCommunication()
        {
            System.Diagnostics.Debug.WriteLine("Testing secure communication...");

            using (var processManager = new SecureProcessManager())
            {
                try
                {
                    // Initialize
                    await processManager.InitializeAsync();
                    System.Diagnostics.Debug.WriteLine("✓ Secure channel established");

                    // Test simple command
                    var testCommand = new SecureCommand("GET_CONFIG");
                    var response = await processManager.SendCommandAsync(testCommand);

                    if (response.Success)
                    {
                        System.Diagnostics.Debug.WriteLine("✓ Command executed successfully");
                        System.Diagnostics.Debug.WriteLine($"Result: {response.Result}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"✗ Command failed: {response.Error}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"✗ Error: {ex.Message}");
                }
            }
        }
    }
}
