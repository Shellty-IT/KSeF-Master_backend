using FluentValidation;
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Nip)
            .NotEmpty().WithMessage("NIP jest wymagany")
            .Length(10).WithMessage("NIP musi mieć dokładnie 10 znaków")
            .Matches(@"^\d{10}$").WithMessage("NIP musi składać się z 10 cyfr");

        RuleFor(x => x.KsefToken)
            .NotEmpty().WithMessage("Token KSeF jest wymagany")
            .Must(t => t.Contains('|')).WithMessage("Nieprawidłowy format tokenu KSeF");
    }
}