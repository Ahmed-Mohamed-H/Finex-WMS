using Finex.Application.DTOs.Production;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.Validators.Production
{
    public sealed class ProducedLineDtoValidator : AbstractValidator<ProducedLineDto>
    {
        public ProducedLineDtoValidator()
        {
            RuleFor(x => x.Sku)
                .NotEmpty()
                .MaximumLength(50);

            RuleFor(x => x.UnitName)
                .NotEmpty()
                .MaximumLength(150);

            RuleFor(x => x.UnitBarcode)
                .NotEmpty()
                .MaximumLength(50);

            RuleFor(x => x.Quantity)
                .GreaterThan(0);

            RuleFor(x => x.ProductionDate)
                .NotEqual(default(DateOnly));

            RuleFor(x => x.ExpiryDate)
                .NotEqual(default(DateOnly))
                .GreaterThanOrEqualTo(x => x.ProductionDate)
                .WithMessage("Expiry date cannot be earlier than production date.");

            RuleFor(x => x.SsccBarcodes)
                .NotNull()
                .Must(x => x.Count <= 500)
                .WithMessage("A single produced line cannot exceed 500 SSCC barcodes.");

            RuleForEach(x => x.SsccBarcodes)
                .NotEmpty()
                .MaximumLength(20);
        }
    }
}
