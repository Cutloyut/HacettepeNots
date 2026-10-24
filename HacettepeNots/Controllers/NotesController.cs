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
            // 1. İstek Geldi mi Kontrolü
            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new { Success = false, Message = "Lütfen geçerli bir dosya seçin." });
            }

            // 2. Uzantı Güvenlik Kontrolü (Sadece PDF)
            var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
            if (extension != ".pdf")
            {
                return BadRequest(new { Success = false, Message = "Güvenlik İhlali: Sadece .pdf uzantılı dosyalar kabul edilir." });
            }

            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            string downloadUrl = string.Empty;

            // 3. AŞAMA: Önce Firebase Storage'a Yükleme ve Linki Alma
            try
            {
                Console.WriteLine("--> Storage yüklemesi başlatıldı: " + request.File.FileName);
                
                using var stream = request.File.OpenReadStream();

                var task = new FirebaseStorage("hacettepenot-be247.appspot.com", new FirebaseStorageOptions
                    {
                        ThrowOnCancel = true
                    })
                    .Child("uploads")
                    .Child(uniqueFileName)
                    .PutAsync(stream);

                // Dosyanın internet üzerinden erişileceği kalıcı URL adresi bekleniyor
                downloadUrl = await task;
                
                Console.WriteLine("--> ✅ Storage yüklemesi başarılı. Alınan Link: " + downloadUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine("--> ❌ STORAGE HATASI: " + ex.ToString());
                return StatusCode(500, new { Success = false, Message = "Firebase Storage yükleme hatası: " + ex.Message });
            }

            // 4. AŞAMA: Storage'dan dönen Linki Veri Modelimize Dahil Etme
            var newNoteRecord = new
            {
                Id = Guid.NewGuid().ToString(),
                CourseCode = request.CourseCode,
                ContentType = request.ContentType,
                Title = request.Title,
                Uploader = request.Uploader,
                FilePath = downloadUrl, // <-- Storage'dan dönen canlı URL buraya ekleniyor!
                CreatedAt = DateTime.UtcNow
            };

            // 5. AŞAMA: Oluşan Bu Tam Paketi Realtime Database'e Yazma
            try
            {
                Console.WriteLine("--> Realtime Database'e kayıt atılıyor...");
                await _firebaseClient
                    .Child("notes")
                    .PostAsync(newNoteRecord);
                
                Console.WriteLine("--> ✅ Database kayıt işlemi başarılı.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("--> ❌ DATABASE HATASI: " + ex.ToString());
                return StatusCode(500, new { Success = false, Message = "Firebase Veritabanı kayıt hatası: " + ex.Message });
            }

            return Ok(new { 
                Success = true, 
                Message = "Dosya önce Storage'a yüklendi, ardından link ile birlikte veritabanına kaydedildi.", 
                Data = newNoteRecord 
            });
        }
    }

    // Flutter'dan gelen multipart verileri karşılayan model
    public class NoteUploadDto
    {
        public string CourseCode { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Uploader { get; set; } = string.Empty;
        public IFormFile? File { get; set; }
    }
}
