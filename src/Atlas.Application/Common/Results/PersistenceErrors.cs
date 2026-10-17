using Atlas.Application.Common.Exceptions;

namespace Atlas.Application.Common.Results;

public static class PersistenceErrors
{
    public static Error From(PersistenceConflictException exception) => exception.Kind switch
    {
        PersistenceConflictKind.DuplicateUsername => Error.Conflict("username_taken", exception.Message),
        PersistenceConflictKind.DuplicateDepartmentName => Error.Conflict("department_name_taken", exception.Message),
        PersistenceConflictKind.DuplicateProfileName => Error.Conflict("profile_name_taken", exception.Message),
        _ => Error.Conflict("concurrent_update", exception.Message)
    };
}
