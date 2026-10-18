using Microsoft.EntityFrameworkCore;

namespace Cabbie.EntityFrameworkCore;

public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="OutboxMessage"/> to <c>cabbie_ef_outbox</c>. Call from <c>OnModelCreating</c>; the
    /// caller owns the migration that creates the table.
    /// </summary>
    public static ModelBuilder AddCabbieOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("cabbie_ef_outbox");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Destination).IsRequired();
            entity.Property(message => message.MessageType).IsRequired();
            entity.Property(message => message.Headers).IsRequired();
            entity.Property(message => message.Body).IsRequired();
            entity.Property(message => message.CreatedAt).IsRequired();
            entity.HasIndex(message => new { message.SentAt, message.CreatedAt })
                .HasDatabaseName("ix_cabbie_ef_outbox_unsent");
        });
        return modelBuilder;
    }
}
