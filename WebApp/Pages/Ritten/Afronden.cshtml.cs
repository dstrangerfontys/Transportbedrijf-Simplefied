using Infrastructure.DataAccess;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace WebApp.Pages.Ritten
{
    public class AfrondenModel : PageModel
    {
        private readonly TransportPlanner _planner;

        public AfrondenModel(TransportPlanner planner)
        {
            _planner = planner;
        }

        [BindProperty, Required, Range(1, int.MaxValue, ErrorMessage = "Voer een geldig rit-ID in.")]
        public int RitId { get; set; }

        [BindProperty, Required, Range(1, int.MaxValue, ErrorMessage = "Geef gereden kilometers op (>0).")]
        public int GeredenKm { get; set; }

        public string? Message { get; set; }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                Message = "Formulier niet correct ingevuld.";
                return Page();
            }

            try
            {
                await _planner.VoltooiRitAsync(RitId, GeredenKm);
                Message = $"Rit #{RitId} succesvol afgerond. (+{GeredenKm} km verwerkt)";
            }
            catch (Exception ex)
            {
                Message = $"Fout bij afronden van rit: {ex.Message}";
            }

            return Page();
        }
    }
}