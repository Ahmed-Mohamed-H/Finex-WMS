using Finex.Application.DTOs;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.Validators
{
    public sealed class ShiftProductionReportDtoValidator : AbstractValidator<ShiftProductionReportDto>
    {
        public ShiftProductionReportDtoValidator()
        {
            RuleFor(x => x.PlantCode)
                .NotEmpty()
                .MaximumLength(50);

            RuleFor(x => x.LineCode)
                .NotEmpty()
                .MaximumLength(50);

            RuleFor(x => x.ShiftCode)
                .NotEmpty()
                .MaximumLength(50);

            RuleFor(x => x.ShiftSupervisorCode)
                .NotEmpty()
                .MaximumLength(50);

            RuleFor(x => x.ProductionDate)
                .NotEqual(default(DateOnly));

            RuleFor(x => x.ShiftStartedAt)
                .NotEqual(default(DateTimeOffset));

            RuleFor(x => x.ShiftEndedAt)
                .NotEqual(default(DateTimeOffset))
                .GreaterThan(x => x.ShiftStartedAt)
                .WithMessage("Shift end time must be after shift start time.");

            RuleFor(x => x.ProducedLines)
                .NotNull()
                .Must(x => x.Count is > 0 and <= 1000)
                .WithMessage("ProducedLines must contain between 1 and 1000 items.");

            RuleForEach(x => x.ProducedLines)
                .SetValidator(new ProducedLineDtoValidator());
        }
    }
}