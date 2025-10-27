using System.ComponentModel.DataAnnotations;
using Core.Domain;
using Infrastructure.DataAccess;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApp.Pages.Ritten
{
    public class BoekModel : PageModel
    {
        private readonly TransportPlanner _planner;

        public BoekModel(TransportPlanner planner)
        {
            _planner = planner;
        }

        public string MinDateString { get; private set; } = "";
        public string MaxDateString { get; private set; } = "";

        [BindProperty]
        public RitInputViewModel Input { get; set; } = new RitInputViewModel();

        public string? Result { get; set; }

        public void OnGet()
        {
            var tomorrow = DateTime.Today.AddDays(1);
            MinDateString = tomorrow.ToString("yyyy-MM-dd");
            MaxDateString = DateTime.Today.AddYears(1).ToString("yyyy-MM-dd");

            Input.Datum = tomorrow;
            Input.Type = "Personen";
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var tomorrow = DateTime.Today.AddDays(1);
            MinDateString = tomorrow.ToString("yyyy-MM-dd");
            MaxDateString = DateTime.Today.AddYears(1).ToString("yyyy-MM-dd");

            // Validatie
            if (Input.Type == "Personen" && (!Input.AantalPersonen.HasValue || Input.AantalPersonen <= 0))
                ModelState.AddModelError(nameof(Input.AantalPersonen), "Vul het aantal personen in (minimaal 1).");

            if (Input.Type == "Vracht" && (!Input.GewichtKg.HasValue || Input.GewichtKg <= 0))
                ModelState.AddModelError(nameof(Input.GewichtKg), "Vul het gewicht in (minimaal 1 kg).");

            if (Input.Datum.Date < tomorrow)
                ModelState.AddModelError(nameof(Input.Datum), "De datum moet minimaal morgen zijn.");

            if (!ModelState.IsValid)
            {
                Result = "Formulier niet correct ingevuld.";
                return Page();
            }

            // Tijdelijke klantId
            int klantId = 1;

            var aanvraag = new RitAanvraag
            {
                KlantId = klantId,
                Datum = Input.Datum,
                Type = Input.Type == "Personen" ? RitType.Personen : RitType.Vracht,
                AfstandKm = Input.AfstandKm,
                AantalPersonen = Input.Type == "Personen" ? Input.AantalPersonen : null,
                GewichtKg = Input.Type == "Vracht" ? Input.GewichtKg : null
            };

            try
            {
                var rit = await _planner.PlanAsync(aanvraag);
                Result = rit is null
                    ? "Geen geschikt voertuig beschikbaar. Aanvraag is afgewezen."
                    : $"Rit succesvol ingepland! Prijs: €{rit.Prijs:0.00}";
            }
            catch (Exception ex)
            {
                Result =
                    $"Er is een fout opgetreden bij het plannen van de rit:<br/>" +
                    $"{ex.GetType().Name}: {ex.Message}<br/>" +
                    $"<pre style=\"white-space:pre-wrap\">{ex.StackTrace}</pre>";
                return Page();
            }

            return Page();
        }

        public class RitInputViewModel
        {
            [Required(ErrorMessage = "Kies een type.")]
            public string Type { get; set; } = "Personen";

            [Required(ErrorMessage = "Voer een afstand in.")]
            [Range(1, int.MaxValue, ErrorMessage = "Afstand moet groter zijn dan 0.")]
            public int AfstandKm { get; set; }

            public int? AantalPersonen { get; set; }

            public int? GewichtKg { get; set; }

            [Required(ErrorMessage = "Kies een datum.")]
            [DataType(DataType.Date)]
            public DateTime Datum { get; set; }
        }
    }
}