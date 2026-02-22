using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace NabdAltamayyuz.Services
{
    // ====================================================
    // 1. نماذج بيانات (DTOs) التقارير (sp-reports)
    // ====================================================
    public class TeleworkReportDto
    {
        [JsonPropertyName("EstLaborOfficeId")]
        public string EstLaborOfficeId { get; set; }

        [JsonPropertyName("EstSequenceNumber")]
        public string EstSequenceNumber { get; set; }

        [JsonPropertyName("IdNumber")]
        public string IdNumber { get; set; }

        [JsonPropertyName("ActivityLevel")]
        public string ActivityLevel { get; set; }

        [JsonPropertyName("LoginCount")]
        public string LoginCount { get; set; }

        [JsonPropertyName("LogoutCount")]
        public string LogoutCount { get; set; }

        [JsonPropertyName("AssignedTasks")]
        public string AssignedTasks { get; set; }

        [JsonPropertyName("CompletedTasks")]
        public string CompletedTasks { get; set; }

        [JsonPropertyName("TotalWorkTime")]
        public string TotalWorkTime { get; set; }
    }

    // ====================================================
    // 2. نماذج بيانات (DTOs) العقود (api/contracts/store)
    // ====================================================
    public class EmployerDto
    {
        public string officeId { get; set; }
        public string sequenceNumber { get; set; }
        public string mobileNumber { get; set; }
        public string nationalAddress { get; set; }
        public string email { get; set; }
    }

    public class ContractJobDto
    {
        public string title { get; set; }
        public string type { get; set; }
        public string description { get; set; }
        public int job_api_id { get; set; }
    }

    public class TeleworkerDto
    {
        public string nid { get; set; }
        public string dateOfBirth { get; set; } // الصيغة المطلوبة: YYYY-MM-DD
    }

    public class AllowanceDto
    {
        public string name { get; set; }
        public string value { get; set; }
    }

    public class SalaryDto
    {
        public string amount { get; set; }
        public string salaryDue { get; set; }
        public string numberOfWorkingDays { get; set; }
        public string vacationsPerYear { get; set; }
        public string type { get; set; }
        public string cityKey { get; set; }
    }

    public class ContractDetailsDto
    {
        public List<AllowanceDto> allowances { get; set; }
        public string startedAt { get; set; }
        public string hoursPerDay { get; set; }
        public SalaryDto salary { get; set; }
    }

    public class ContractRequestDto
    {
        public EmployerDto employer { get; set; }
        public ContractJobDto contractJob { get; set; }
        public TeleworkerDto teleworker { get; set; }
        public ContractDetailsDto contract { get; set; }
    }

    // ====================================================
    // 3. نماذج بيانات (DTOs) القوائم المنسدلة
    // ====================================================
    public class CityDto
    {
        public string id { get; set; }
        public string key { get; set; }
        public string name_ar { get; set; }
        public string name_en { get; set; }
    }

    public class JobDefinitionDto
    {
        public string id { get; set; }
        public string name_ar { get; set; }
        public string name_en { get; set; }
    }

    // ====================================================
    // واجهة الخدمة (Interface)
    // ====================================================
    public interface ITeleworksService
    {
        Task<string> AuthenticateAsync(int clientId, string clientToken);
        Task<bool> CreateContractAsync(ContractRequestDto contractRequest, string bearerToken);
        Task<bool> SendReportsBatchAsync(List<TeleworkReportDto> reports);
        Task<List<CityDto>> GetCitiesAsync(string bearerToken);
        Task<List<JobDefinitionDto>> GetJobTypesAsync(string bearerToken);
        Task<List<JobDefinitionDto>> GetJobTitlesAsync(string bearerToken);
        Task<bool> SendAttendanceAsync(string employeeId, DateTime date, DateTime timeIn, DateTime? timeOut);
    }

    // ====================================================
    // تطبيق الخدمة (Implementation)
    // ====================================================
    public class TeleworksService : ITeleworksService
    {
        private readonly HttpClient _httpClient;
        private readonly string _spToken;
        private readonly string _providerOfficeId;
        private readonly string _providerSequence;
        private readonly JsonSerializerOptions _jsonOptions;

        public TeleworksService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;

            var baseUrl = configuration["Teleworks:BaseUrl"] ?? "https://teleworks.sa/";
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            _httpClient.BaseAddress = new Uri(baseUrl);

            _spToken = configuration["Teleworks:ApiKey"];

            // جلب بيانات "نبض التميز" (المزود) وتجهيزها للترويسات
            _providerOfficeId = configuration["Teleworks:ServiceProviderOfficeId"]?.Split('-')[0].Trim();
            _providerSequence = configuration["Teleworks:ServiceProviderSequence"]?.Replace("-", "").Trim();

            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        // 1. المصادقة (Authentication)
        public async Task<string> AuthenticateAsync(int clientId, string clientToken)
        {
            try
            {
                var payload = new { client_id = clientId, client_token = clientToken };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                _httpClient.DefaultRequestHeaders.Clear();
                var response = await _httpClient.PostAsync("api/authenticate", content);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    return result.Trim('"');
                }
                return null;
            }
            catch { return null; }
        }

        // 2. تسجيل العقد (Store Contract)
        public async Task<bool> CreateContractAsync(ContractRequestDto contractRequest, string bearerToken)
        {
            try
            {
                var content = new StringContent(JsonSerializer.Serialize(contractRequest), Encoding.UTF8, "application/json");
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Securityschemes", bearerToken);
                var response = await _httpClient.PostAsync("api/contracts/store", content);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // 3. إرسال التقارير المجمعة (sp-reports.php)
        public async Task<bool> SendReportsBatchAsync(List<TeleworkReportDto> reports)
        {
            try
            {
                // تنظيف البيانات لضمان القبول (حذف الشرطات والمسافات من الأرقام)
                foreach (var report in reports)
                {
                    if (!string.IsNullOrEmpty(report.EstLaborOfficeId))
                        report.EstLaborOfficeId = report.EstLaborOfficeId.Split('-')[0].Trim();

                    if (!string.IsNullOrEmpty(report.EstSequenceNumber))
                        report.EstSequenceNumber = report.EstSequenceNumber.Replace("-", "").Trim();

                    report.IdNumber = report.IdNumber?.Trim();
                }

                var jsonBody = JsonSerializer.Serialize(reports);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();

                // الترويسات تحمل بيانات "نبض التميز" (صاحب التوكن)
                _httpClient.DefaultRequestHeaders.Add("Sp-Token", _spToken?.Trim());
                _httpClient.DefaultRequestHeaders.Add("Est-Labor-Office-Id", _providerOfficeId);
                _httpClient.DefaultRequestHeaders.Add("Est-Sequence-Number", _providerSequence);

                Console.WriteLine($"[Teleworks Debug] JSON Sent: {jsonBody}");

                var response = await _httpClient.PostAsync("sp-reports.php", content);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Teleworks API] Success: {reports.Count} records accepted.");
                    return true;
                }

                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[Teleworks API Error] {response.StatusCode} - {error}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Teleworks API Exception] {ex.Message}");
                return false;
            }
        }

        // 4. جلب القوائم (المدن، أنواع الوظائف، المسميات)
        public async Task<List<CityDto>> GetCitiesAsync(string bearerToken)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Securityschemes", bearerToken);
                var response = await _httpClient.GetAsync("api/cities");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<CityDto>>(content, _jsonOptions);
                }
                return new List<CityDto>();
            }
            catch { return new List<CityDto>(); }
        }

        public async Task<List<JobDefinitionDto>> GetJobTypesAsync(string bearerToken)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Securityschemes", bearerToken);
                var response = await _httpClient.GetAsync("api/job_types");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<JobDefinitionDto>>(content, _jsonOptions);
                }
                return new List<JobDefinitionDto>();
            }
            catch { return new List<JobDefinitionDto>(); }
        }

        public async Task<List<JobDefinitionDto>> GetJobTitlesAsync(string bearerToken)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Securityschemes", bearerToken);
                var response = await _httpClient.GetAsync("api/job_titles");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<JobDefinitionDto>>(content, _jsonOptions);
                }
                return new List<JobDefinitionDto>();
            }
            catch { return new List<JobDefinitionDto>(); }
        }

        public Task<bool> SendAttendanceAsync(string employeeId, DateTime date, DateTime timeIn, DateTime? timeOut)
        {
            return Task.FromResult(true);
        }
    }
}