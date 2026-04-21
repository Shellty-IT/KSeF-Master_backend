using FluentValidation;
using KSeF.Backend.Models.Requests;

namespace KSeF.Backend.Validators;

public class InvoiceQueryRequestValidator : AbstractValidator<InvoiceQueryRequest>
{
    private static readonly string[] ValidSubjectTypes = ["Subject1", "Subject2"];
    private static readonly string[] ValidDateTypes = ["InvoicingDate", "PermanentStorage", "AcquisitionTimestamp"];

    public InvoiceQueryRequestValidator()
    {
        RuleFor(x => x.SubjectType)
            .NotEmpty().WithMessage("SubjectType jest wymagany (Subject1 lub Subject2)")
            .Must(t => ValidSubjectTypes.Contains(t))
            .WithMessage("SubjectType musi być 'Subject1' (wystawione) lub 'Subject2' (odebrane)");

        RuleFor(x => x.DateRange)
            .NotNull().WithMessage("DateRange jest wymagany");

        When(x => x.DateRange != null, () =>
        {
            RuleFor(x => x.DateRange!.From)
                .NotEqual(default(DateTime)).WithMessage("DateRange.From jest wymagany");

            RuleFor(x => x.DateRange!.To)
                .NotEqual(default(DateTime)).WithMessage("DateRange.To jest wymagany");

            RuleFor(x => x.DateRange!)
                .Must(d => d.From <= d.To)
                .WithMessage("DateRange.From nie może być późniejszy niż DateRange.To")
                .Must(d => d.To - d.From <= TimeSpan.FromDays(366))
                .WithMessage("Zakres dat nie może przekraczać 12 miesięcy");

            RuleFor(x => x.DateRange!.DateType)
                .NotEmpty().WithMessage("DateRange.DateType jest wymagany")
                .Must(t => ValidDateTypes.Contains(t))
                .WithMessage($"DateRange.DateType musi być jednym z: {string.Join(", ", ValidDateTypes)}");
        });

        When(x => x.AmountFrom.HasValue && x.AmountTo.HasValue, () =>
        {
            RuleFor(x => x)
                .Must(x => x.AmountFrom <= x.AmountTo)
                .WithMessage("AmountFrom nie może być większy niż AmountTo");
        });

        When(x => x.MaxResults.HasValue, () =>
        {
            RuleFor(x => x.MaxResults!.Value)
                .GreaterThanOrEqualTo(1).WithMessage("MaxResults musi być >= 1");
        });
    }
}