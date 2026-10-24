using Microsoft.AspNetCore.Mvc;
using Firebase.Database;
using Firebase.Database.Query;
using Firebase.Storage;

namespace HacettepeNot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotesController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        
        // Firebase Realtime Database İstemcisi
        private readonly FirebaseClient _firebaseClient = new("https://hacettepenot-be247-default-rtdb.firebaseio.com/");

        public NotesController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadNote([FromForm] NoteUploadDto request)
        {
            Console.WriteLine("==========================================");
            Console.WriteLine("==> /api/notes/upload ENDPOINTINE İSTEK GELDİ! <==");
            Console.WriteLine($"==> Başlık: {request.Title}, Ders: {request.CourseCode}");
            
            if (request.File != null)
            {
                Console.WriteLine($"==> Gelen Dosya: {request.File.FileName} ({request.File.Length} bayt)");
            }
            else
            {
                Console.WriteLine("==> ❌ UYARI: Gelen dosya (File) NULL!");
            }
            Console.WriteLine("==========================================");

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

            // 1. AŞAMA: Firebase Storage'a Yükleme (Bucket Adı Doğrulanmış Hali)
            try
            {
                Console.WriteLine("--> Firebase Storage'a yükleme başlatılıyor...");
                using var stream = request.File.OpenReadStream();

                var task = new FirebaseStorage(
                    "hacettepenot-be247.appspot.com", 
                    new FirebaseStorageOptions
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

            // 2. AŞAMA: Veri Paketini Oluşturma
            var newNoteRecord = new
            {
                Id = Guid.NewGuid().ToString(),
                CourseCode = request.CourseCode,
                ContentType = request.ContentType,
                Title = request.Title,
                Uploader = request.Uploader,
                FilePath = downloadUrl,
                CreatedAt = DateTime.UtcNow
            };

            // 3. AŞAMA: Realtime Database'e Kayıt Atma
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
    }

    public class NoteUploadDto
    {
        public string CourseCode { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Uploader { get; set; } = string.Empty;
        public IFormFile? File { get; set; }
    }
}
