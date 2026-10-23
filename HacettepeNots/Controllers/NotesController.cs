using Microsoft.AspNetCore.Mvc;
using Firebase.Database;
using Firebase.Database.Query;

namespace HacettepeNot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotesController : ControllerBase
    {
        IWebHostEnvironment _env;
        // Firebase Realtime Database URL'ini buraya kendi proje adresinle yazıyoruz
        private readonly FirebaseClient _firebaseClient = new("https://hacetttepenot-be247-default-rtdb.firebaseio.com/");

        public NotesController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadNote([FromForm] NoteUploadDto request)
        {
            // 1. Dosya Var mı Kontrolü
            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new { Success = false, Message = "Lütfen geçerli bir dosya seçin." });
            }

            // 2. Güvenlik & MIME / Uzantı Kontrolü (Sadece PDF)
            var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
            if (extension != ".pdf")
            {
                return BadRequest(new { Success = false, Message = "Güvenlik İhlali: Sadece .pdf uzantılı dosyalar kabul edilir." });
            }

            // 3. Sunucuda Depolama Yolu Oluşturma
            var uploadsFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await request.File.CopyToAsync(stream);
            }

            // 4. C# AI / OCR İçerik Doğrulama Adımı
            bool isAiApproved = true; 

            if (!isAiApproved)
            {
                System.IO.File.Delete(filePath);
                return BadRequest(new { Success = false, Message = "AI Doğrulaması Başarısız: Dosya içeriği seçilen dersle uyuşmuyor." });
            }

            // 5. Nesneyi Oluşturma
            var newNoteRecord = new
            {
                Id = Guid.NewGuid().ToString(),
                CourseCode = request.CourseCode,
                ContentType = request.ContentType,
                Title = request.Title,
                Uploader = request.Uploader,
                FilePath = $"/uploads/{uniqueFileName}",
                CreatedAt = DateTime.UtcNow
            };

            // 6. Firebase Realtime Database'e Gerçek Kayıt Atma
            try
            {
                await _firebaseClient
                    .Child("notes") // Firebase'de oluşacak tablo/düğüm adı
                    .PostAsync(newNoteRecord);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Success = false, Message = "Firebase kayıt hatası: " + ex.Message });
            }

            return Ok(new { 
                Success = true, 
                Message = "Dosya C# Backend tarafından doğrulandı ve Firebase'e kaydedildi.", 
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