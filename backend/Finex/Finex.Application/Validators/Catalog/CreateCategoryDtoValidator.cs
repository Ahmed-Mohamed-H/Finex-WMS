using Finex.Application.DTOs.Catalog;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Finex.Application.Validators.Catalog
{
    public sealed class CreateCategoryDtoValidator : AbstractValidator<CreateCategoryDto>
    {
        public CreateCategoryDtoValidator()
        {
            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Category code is required.")
                .MaximumLength(50).WithMessage("Category code cannot exceed 50 characters.")
                .Matches("^[A-Za-z0-9_-]+$").WithMessage("Category code can only contain English letters, numbers, hyphens, and underscores.");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Category name is required.")
                .MaximumLength(150).WithMessage("Category name cannot exceed 150 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");

            RuleFor(x => x.ParentCategoryId)
                .GreaterThan(0).When(x => x.ParentCategoryId.HasValue)
                .WithMessage("ParentCategoryId must be a positive integer.");
        }
    }
}
