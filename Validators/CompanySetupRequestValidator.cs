using FluentValidation;
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Validators;

public class CompanySetupRequestValidator : AbstractValidator<CompanySetupRequest>
{
    private static readonly string[] AllowedEnvironments = ["Test", "Production"];

    public CompanySetupRequestValidator()
    {
        RuleFor(x => x.CompanyName)
            .NotEmpty().WithMessage("Nazwa firmy jest wymagana")
            .MaximumLength(500).WithMessage("Nazwa firmy nie może przekraczać 500 znaków");

        RuleFor(x => x.Nip)
            .NotEmpty().WithMessage("NIP jest wymagany")
            .Length(10).WithMessage("NIP musi mieć dokładnie 10 znaków")
            .Matches(@"^\d{10}$").WithMessage("NIP musi składać się z 10 cyfr");

        RuleFor(x => x.KsefToken)
            .NotEmpty().WithMessage("Token KSeF jest wymagany")
            .Must(t => t.Contains('|')).WithMessage("Nieprawidłowy format tokenu KSeF");

        RuleFor(x => x.KsefEnvironment)
            .NotEmpty().WithMessage("Środowisko KSeF jest wymagane")
            .Must(e => AllowedEnvironments.Contains(e))
            .WithMessage("Dozwolone środowiska: 'Test' lub 'Production'");
    }
}