using Finex.Application.DTOs.Catalog;
using FluentValidation;

namespace Finex.Application.Validators.Catalog;

public sealed class MoveCategoryDtoValidator : AbstractValidator<MoveCategoryDto>
{
    public MoveCategoryDtoValidator()
    {
        RuleFor(x => x.NewParentCategoryId)
            .GreaterThan(0)
            .When(x => x.NewParentCategoryId.HasValue)
            .WithMessage("NewParentCategoryId must be a positive integer.");
    }
}