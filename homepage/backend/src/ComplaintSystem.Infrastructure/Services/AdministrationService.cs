using ComplaintSystem.Application.Abstractions;
using ComplaintSystem.Application.Common;
using ComplaintSystem.Application.Contracts;
using ComplaintSystem.Domain.Entities;
using ComplaintSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ComplaintSystem.Infrastructure.Services;

public sealed class AdministrationService(ApplicationDbContext dbContext, IAuditService auditService) : IAdministrationService
{
    public async Task<IReadOnlyCollection<DepartmentDto>> GetDepartmentsAsync(CancellationToken cancellationToken) =>
        await dbContext.Departments.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new DepartmentDto(x.Id, x.Code, x.Name, x.ContactEmail, x.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<Result<DepartmentDto>> UpsertDepartmentAsync(Guid actorId, Guid? id, DepartmentRequest request,
        CancellationToken cancellationToken)
    {
        Department department;
        if (id.HasValue)
        {
            department = await dbContext.Departments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new NotFoundException("Department not found.");
            department.Code = request.Code.Trim().ToUpperInvariant();
            department.Name = request.Name.Trim();
            department.ContactEmail = request.ContactEmail?.Trim().ToLowerInvariant();
            department.IsActive = request.IsActive;
        }
        else
        {
            department = new Department
            {
                Code = request.Code.Trim().ToUpperInvariant(),
                Name = request.Name.Trim(),
                ContactEmail = request.ContactEmail?.Trim().ToLowerInvariant(),
                IsActive = request.IsActive
            };
            dbContext.Departments.Add(department);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(actorId, "DepartmentSaved", "Department", department.Id.ToString(), true,
            new RequestMetadata("system", null), new { department.Code, department.Name }, cancellationToken);
        return Result<DepartmentDto>.Success(new DepartmentDto(department.Id, department.Code, department.Name,
            department.ContactEmail, department.IsActive));
    }

    public async Task<Result> DeleteDepartmentAsync(Guid actorId, Guid id, CancellationToken cancellationToken)
    {
        var department = await dbContext.Departments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Department not found.");
        if (await dbContext.Complaints.AnyAsync(x => x.DepartmentId == id, cancellationToken))
        {
            return Result.Failure("department_in_use", "Deactivate this department because complaint records already reference it.");
        }
        dbContext.Departments.Remove(department);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(actorId, "DepartmentDeleted", "Department", id.ToString(), true,
            new RequestMetadata("system", null), new { }, cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyCollection<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        await dbContext.ComplaintCategories.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new CategoryDto(x.Id, x.Name, x.Description, x.DepartmentId, x.DefaultPriority,
                x.AllowsAnonymous, x.IsActive)).ToListAsync(cancellationToken);

    public async Task<Result<CategoryDto>> UpsertCategoryAsync(Guid actorId, Guid? id, CategoryRequest request,
        CancellationToken cancellationToken)
    {
        ComplaintCategory category;
        if (id.HasValue)
        {
            category = await dbContext.ComplaintCategories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new NotFoundException("Category not found.");
            category.Name = request.Name.Trim();
            category.Description = request.Description?.Trim();
            category.DepartmentId = request.DepartmentId;
            category.DefaultPriority = request.DefaultPriority;
            category.AllowsAnonymous = request.AllowsAnonymous;
            category.IsActive = request.IsActive;
        }
        else
        {
            category = new ComplaintCategory
            {
                Name = request.Name.Trim(), Description = request.Description?.Trim(),
                DepartmentId = request.DepartmentId, DefaultPriority = request.DefaultPriority,
                AllowsAnonymous = request.AllowsAnonymous, IsActive = request.IsActive
            };
            dbContext.ComplaintCategories.Add(category);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(actorId, "ComplaintCategorySaved", "ComplaintCategory", category.Id.ToString(), true,
            new RequestMetadata("system", null), new { category.Name }, cancellationToken);
        return Result<CategoryDto>.Success(new CategoryDto(category.Id, category.Name, category.Description,
            category.DepartmentId, category.DefaultPriority, category.AllowsAnonymous, category.IsActive));
    }

    public async Task<Result> DeleteCategoryAsync(Guid actorId, Guid id, CancellationToken cancellationToken)
    {
        var category = await dbContext.ComplaintCategories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Category not found.");
        if (await dbContext.Complaints.AnyAsync(x => x.CategoryId == id, cancellationToken))
        {
            return Result.Failure("category_in_use", "Deactivate this category because complaint records already reference it.");
        }
        dbContext.ComplaintCategories.Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.WriteAsync(actorId, "ComplaintCategoryDeleted", "ComplaintCategory", id.ToString(), true,
            new RequestMetadata("system", null), new { }, cancellationToken);
        return Result.Success();
    }
}
