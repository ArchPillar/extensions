using Microsoft.EntityFrameworkCore;

namespace Command.WebApiSample.Notes;

internal sealed class NotesDbContext(DbContextOptions<NotesDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Note>(builder =>
        {
            builder.HasKey(note => note.Id);
            builder.Property(note => note.Title).IsRequired().HasMaxLength(120);
            builder.Property(note => note.Body).IsRequired().HasMaxLength(4_000);
            builder.HasIndex(note => note.IsArchived);
        });
    }
}
