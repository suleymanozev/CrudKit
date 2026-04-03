namespace CrudKit.Core.Enums;

/// <summary>Permission scope — determines which records are accessible.</summary>
public enum PermScope
{
    Own,           // Only the user's own records
    Department,    // Records within the user's department
    All            // All records
}
