using ArchPillar.Extensions.Localization;

namespace Localization.TodoSample;

/// <summary>
/// The UI strings for the to-do app, as a self-scoped bundle. The member name is the key (via
/// <c>[CallerMemberName]</c>) and the category is this type's full name, so the German/French catalogs
/// live under <c>Localization.TodoSample.TodoStrings</c>.
/// </summary>
public sealed class TodoStrings(ILocalizer<TodoStrings> localizer) : Localized<TodoStrings>(localizer)
{
    public string Title => Translate("My Tasks");

    public string AddHint => Translate("Type a task and press Enter to add it.");

    public string Empty => Translate("Nothing to do — add your first task!");

    public string Remaining(int count) =>
        Translate("{count, plural, =0 {All done!} one {# task left} other {# tasks left}}", [("count", count)]);
}
