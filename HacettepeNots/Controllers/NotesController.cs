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
                
                // Realtime Database "notes" düğümündeki tüm kayıtları okuyoruz
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
                return BadRequest(new { Success = false, Message = "Lütfen geçerli bir dosya seçin." });
            }

            var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
            if (extension != ".pdf")
            {
                return BadRequest(new { Success = false, Message = "Sadece .pdf uzantılı dosyalar kabul edilir." });
            }

            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            string downloadUrl = string.Empty;

            // Step 1: Firebase Storage'a yükleme yapıp URL alıyoruz
            try
            {
                Console.WriteLine($"--> Firebase Storage ({FirebaseBucket}) yüklemesi başlatılıyor...");
                using var stream = request.File.OpenReadStream();

                var task = new FirebaseStorage(
                    FirebaseBucket,
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
                return StatusCode(500, new { Success = false, Message = "Firebase Storage hatası: " + ex.Message });
            }

            // Step 2: Dönen URL ile veri modelini hazırlıyoruz
            var newNoteRecord = new NoteRecordDto
            {
                Id = Guid.NewGuid().ToString(),
                CourseCode = request.CourseCode,
                ContentType = request.ContentType,
                Title = request.Title,
                Uploader = request.Uploader,
                FilePath = downloadUrl,
                CreatedAt = DateTime.UtcNow
            };

            // Step 3: Realtime Database'e yazıyoruz
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
                return StatusCode(500, new { Success = false, Message = "Firebase DB hatası: " + ex.Message });
            }

            return Ok(new { 
                Success = true, 
                Message = "Dosya başarıyla Storage'a yüklendi ve veritabanına kaydedildi.", 
                Data = newNoteRecord 
            });
        }
    }

    // Flutter'dan dosya gönderirken kullanılan DTO
    public class NoteUploadDto
    {
        public string CourseCode { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Uploader { get; set; } = string.Empty;
        public IFormFile? File { get; set; }
    }

    // Realtime Database kayıt ve okuma DTO'su
    public class NoteRecordDto
    {
        public string Id { get; set; } = string.Empty;
        public string CourseCode { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Uploader { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
