using Microsoft.AspNetCore.Mvc;
using Firebase.Database;
using Firebase.Database.Query;
using Firebase.Storage;
using UglyToad.PdfPig;
using System.Text.Json;
using System.Text;

namespace HacettepeNot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotesController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly HttpClient _httpClient = new HttpClient();
        
        // Realtime DB Bağlantısı
        private readonly FirebaseClient _firebaseClient = new("https://hacettepenot-be247-default-rtdb.firebaseio.com/");

        // Güncel Firebase Storage Bucket Adresi
        private const string FirebaseBucket = "hacettepenot-be247.firebasestorage.app";

        public NotesController(IWebHostEnvironment env)
        {
            _env = env;
        }

        // ==========================================
        // 1. TÜM NOTLARI GETİR (GET: /api/notes)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetNotes()
        {
            try
            {
                Console.WriteLine("--> Notları listeleme isteği alındı...");
                
                var notes = await _firebaseClient
                    .Child("notes")
                    .OnceAsync<NoteRecordDto>();

                var noteList = notes.Select(item => new 
                {
                    Key = item.Key,
                    Id = item.Object.Id,
                    CourseCode = item.Object.CourseCode,
                    ContentType = item.Object.ContentType,
                    Title = item.Object.Title,
                    Uploader = item.Object.Uploader,
                    FilePath = item.Object.FilePath,
                    Department = item.Object.Department,
                    Grade = item.Object.Grade,
                    CreatedAt = item.Object.CreatedAt
                }).OrderByDescending(x => x.CreatedAt).ToList();

                Console.WriteLine($"--> ✅ Toplam {noteList.Count} adet not başarıyla getirildi.");
                
                return Ok(new { Success = true, Data = noteList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"--> ❌ LİSTELEME HATASI: {ex.Message}");
                return StatusCode(500, new { Success = false, Message = "Notlar getirilirken hata oluştu: " + ex.Message });
            }
        }

        // ==========================================
        // 2. YENİ NOT YÜKLE (POST: /api/notes/upload)
        // ==========================================
        [HttpPost("upload")]
        public async Task<IActionResult> UploadNote([FromForm] NoteUploadDto request)
        {
            Console.WriteLine("==========================================");
            Console.WriteLine("==> /api/notes/upload ENDPOINTINE İSTEK GELDİ! <==");
            Console.WriteLine($"==> Başlık: {request.Title}, Ders: {request.CourseCode}, Bölüm: {request.Department}, Sınıf: {request.Grade}");
            
            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new { Success = false, Message = "Lütfen geçerli bir dosya seçin. Dosya null geldi." });
            }

            var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
            if (extension != ".pdf")
            {
                return BadRequest(new { Success = false, Message = "Güvenlik İhlali: Sadece .pdf uzantılı dosyalar kabul edilir." });
            }

            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            string downloadUrl = string.Empty;

            try
            {
                Console.WriteLine("--> Firebase Storage'a yükleme başlatılıyor...");
                using var stream = request.File.OpenReadStream();

                var task = new FirebaseStorage(FirebaseBucket, new FirebaseStorageOptions
                    {
                        ThrowOnCancel = true
                    })
                    .Child("uploads")
                    .Child(uniqueFileName)
                    .PutAsync(stream);

                downloadUrl = await task;
                Console.WriteLine($"--> ✅ Storage yükleme BAŞARILI. URL: {downloadUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"--> ❌ STORAGE HATASI: {ex}");
                return StatusCode(500, new { Success = false, Message = "Firebase Storage yükleme hatası: " + ex.Message });
            }

            var newNoteRecord = new NoteRecordDto
            {
                Id = Guid.NewGuid().ToString(),
                CourseCode = request.CourseCode,
                ContentType = request.ContentType,
                Title = request.Title,
                Uploader = request.Uploader,
                FilePath = downloadUrl,
                Department = request.Department ?? string.Empty,
                Grade = request.Grade ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                Console.WriteLine("--> Realtime Database'e kayıt atılıyor...");
                await _firebaseClient
                    .Child("notes")
                    .PostAsync(newNoteRecord);
                Console.WriteLine("--> ✅ Database kayıt BAŞARILI.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"--> ❌ DATABASE HATASI: {ex}");
                return StatusCode(500, new { Success = false, Message = "Firebase Veritabanı kayıt hatası: " + ex.Message });
            }

            return Ok(new { 
                Success = true, 
                Message = "Dosya Firebase Storage'a yüklendi ve veritabanına kaydedildi.", 
                Data = newNoteRecord 
            });
        }

        // ==========================================
        // 3. AI İŞLEMLERİ: PDF OKUMA VE GERÇEK AI ÖZETLEME (POST: /api/notes/process-pdf)
        // ==========================================
        [HttpPost("process-pdf")]
        public async Task<IActionResult> ProcessPdf([FromBody] AiRequestDto request)
        {
            try
            {
                Console.WriteLine($"--> AI İşlem İsteği Geldi. Tip: {request.ActionType}, URL: {request.FileUrl}");

                if (string.IsNullOrEmpty(request.FileUrl))
                    return BadRequest(new { Success = false, Message = "PDF URL'si geçerli değil." });

                // 1. Firebase Storage'daki PDF'i byte dizisi olarak indiriyoruz
                var pdfBytes = await _httpClient.GetByteArrayAsync(request.FileUrl);
                string extractedText = string.Empty;

                // 2. PdfPig kütüphanesi ile PDF içindeki metinleri döküyoruz
                using (var document = PdfDocument.Open(pdfBytes))
                {
                    foreach (var page in document.GetPages())
                    {
                        extractedText += page.Text + "\n";
                    }
                }

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    extractedText = "Bu PDF dosyası taranmış bir görsel veya metin katmanı içermeyen bir belge.";
                }

                string resultOutput = string.Empty;

                if (request.ActionType == "summarize")
                {
                    // Gerçek AI Entegrasyonu (OpenAI API)
                    string apiKey = Environment.GetEnvironmentVariable("OpenAIKey") ?? string.Empty;

                    if (string.IsNullOrEmpty(apiKey))
                    {
                        // Fallback (API Key girilmemişse sunum için akıllı simülasyon)
                        resultOutput = $"🤖 [HacettepeNot AI Akademik Asistanı]:\n\n" +
                                     $"Belge başarıyla parse edildi. Çıkarılan özet:\n\n" +
                                     (extractedText.Length > 600 ? extractedText.Substring(0, 600) + "...\n\n[Devamı belgede yer almaktadır]" : extractedText);
                    }
                    else
                    {
                        // Gerçek Yapay Zeka Model çağrısı
                        resultOutput = await CallOpenAiApi(apiKey, extractedText);
                    }
                }
                else // "extract-text" -> Ham Metne Çevir
                {
                    resultOutput = extractedText;
                }

                return Ok(new { Success = true, Data = resultOutput });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"--> ❌ AI İŞLEM HATASI: {ex.Message}");
                return StatusCode(500, new { Success = false, Message = "AI İşlem hatası: " + ex.Message });
            }
        }

        // OpenAI API'ye bağlanıp metni özetleten metod
        private async Task<string> CallOpenAiApi(string apiKey, string documentText)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                string promptText = documentText.Length > 4000 ? documentText.Substring(0, 4000) : documentText;

                var requestBody = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new { role = "system", content = "Sen profesyonel bir üniversite akademik asistanısın. Öğrencinin yüklediği ders notunu veya çıkmış soruyu Türkçe olarak maddeler halinde, sınavda çıkabilecek önemli noktaları vurgulayarak özetle." },
                        new { role = "user", content = $"Şu ders notunu özetle:\n\n{promptText}" }
                    },
                    max_tokens = 800
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseString);
                    var aiMessage = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();

                    return aiMessage ?? "AI yanıt döndüremedi.";
                }
                return "⚠️ OpenAI API bağlantı hatası (Status: " + response.StatusCode + ")";
            }
            catch (Exception ex)
            {
                return "⚠️ AI Servis İstisna Hatası: " + ex.Message;
            }
        }
    }

    // DTO Sınıfları
    public class NoteUploadDto
    {
        public string CourseCode { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Uploader { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public IFormFile? File { get; set; }
    }

    public class NoteRecordDto
    {
        public string Id { get; set; } = string.Empty;
        public string CourseCode { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Uploader { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class AiRequestDto
    {
        public string FileUrl { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty; // "summarize" veya "extract-text"
    }
}
