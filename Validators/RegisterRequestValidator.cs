using FluentValidation;
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email jest wymagany")
            .EmailAddress().WithMessage("Nieprawidłowy format email");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Hasło jest wymagane")
            .MinimumLength(8).WithMessage("Hasło musi mieć co najmniej 8 znaków");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Imię i nazwisko jest wymagane")
            .MaximumLength(200).WithMessage("Imię i nazwisko nie może przekraczać 200 znaków");
    }
}