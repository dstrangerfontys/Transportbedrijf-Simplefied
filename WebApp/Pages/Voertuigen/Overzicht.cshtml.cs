using Core.Domain;
using Infrastructure.DataAccess;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApp.Pages.Voertuigen
{
    public class OverzichtModel : PageModel
    {
        private readonly MySqlConnectionFactory _factory;
        private readonly VoertuigRepository _voertuigRepo;

        public OverzichtModel(MySqlConnectionFactory factory, VoertuigRepository repo)
        {
            _factory = factory;
            _voertuigRepo = repo;
        }

        public List<Voertuig> Voertuigen { get; private set; } = new();

        public async Task OnGetAsync()
        {
            using var conn = _factory.Create();
            await conn.OpenAsync();
            Voertuigen = await _voertuigRepo.GetAllAsync(conn);
        }
    }
}