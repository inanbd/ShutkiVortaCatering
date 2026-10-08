namespace ShutkiVorta.Domain.Common;

/// <summary>Base type for persisted aggregates and entities with an integer identity key.</summary>
public abstract class Entity
{
    public int Id { get; protected set; }

    public bool IsTransient => Id == 0;

    /// <summary>Assigns the database-generated key after an insert. Used by the persistence layer only.</summary>
    public void AssignId(int id)
    {
        if (!IsTransient)
        {
            throw new InvalidOperationException($"{GetType().Name} already has an identity ({Id}).");
        }

        Id = id;
    }
}
