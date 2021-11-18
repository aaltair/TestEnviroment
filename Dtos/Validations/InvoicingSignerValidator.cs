using API.Extensions;
using FluentValidation;

namespace API.Dtos.Validations
{
    public class InvoicingSignerValidator :AbstractValidator<InvoicingSignerDto>
    {
        public InvoicingSignerValidator()
        {
            RuleFor(w => w.Token).NotEmpty();
            RuleFor(w => w.JsonString).NotEmpty().Must(w => w.ValidateJSON());
        }
    }
}