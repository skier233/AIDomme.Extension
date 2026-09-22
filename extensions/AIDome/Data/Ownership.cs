namespace AIDome.Data;

/// <summary>A row that belongs to exactly one Cove account.</summary>
public interface IUserOwned
{
    int UserId { get; set; }
}

/// <summary>
/// A library definition (method, punishment ending): a shared default with a null owner, a personal
/// override of a shared default, or a personal definition. Consumers see the effective set.
/// </summary>
public interface IDefinition<T> where T : class
{
    int Id { get; set; }
    int? UserId { get; set; }
    int? BaseId { get; set; }
    T AsDefinition(int id);
}
