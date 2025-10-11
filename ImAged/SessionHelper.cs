using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace ImAged.Helpers
{
    public static class SessionHelper
    {
        private static readonly string SessionFilePath = @"E:\APSI PROJECT\IndieSIS-ImAged\image-backend\session\session.txt";
        private static readonly string VerifyUrl = "http://localhost:8080/api/login/verify";

        public static async Task<bool> IsSessionValidAsync()
        {
            if (!File.Exists(SessionFilePath)) return false;

            var token = File.ReadAllText(SessionFilePath).Trim();
            if (string.IsNullOrEmpty(token)) return false;

            using var client = new HttpClient();
            var response = await client.GetAsync($"{VerifyUrl}?token={token}");

            return response.IsSuccessStatusCode;
        }
    }
}
