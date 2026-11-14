using PantryPress.Domain.Modules.Accounts.Contracts;
using PantryPress.Domain.Modules.Recipes.Contracts;
using PantryPress.Domain.Modules.Sites.Contracts;

namespace PantryPress.Domain.Modules.Recipes.Business;

/// <summary>A part of a recipe as a save names it: its id - the recipe's own, or minted for a new part - and its text.</summary>
/// <param name="Id">The part's id.</param>
/// <param name="Claimed">Whether the caller sent the id, so it must be one of this recipe's parts.</param>
/// <param name="Text">The line or step exactly as sent.</param>
internal sealed record RecipeEditPart(Guid Id, bool Claimed, string Text);

/// <summary>A group as a save names it.</summary>
internal sealed record RecipeEditGroup(Guid Id, bool Claimed, string? Heading, IReadOnlyList<RecipeEditPart> Parts);

/// <summary>
/// A whole recipe as one save asks for it (PP4.11), every id already decided: a new part's is minted by the facade before
/// any attempt, so a retried attempt asks for exactly the same rows. Nutrition is not here: its columns are fractional, and
/// this module's Business names no fractional type (PP-D05), so it is written and read beside the columns.
/// </summary>
internal sealed record RecipeEdit(
    string Title,
    string? Summary,
    Quantity? Yield,
    string? YieldUnit,
    int? PrepMinutes,
    int? CookMinutes,
    int? AdditionalMinutes,
    string? AdditionalTimeLabel,
    string? VideoUrl,
    IReadOnlyList<RecipeEditGroup> IngredientGroups,
    IReadOnlyList<RecipeEditGroup> StepGroups,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Courses,
    IReadOnlyList<string> Cuisines,
    IReadOnlyList<string> Diets,
    IReadOnlyList<string> Allergens)
{
    /// <summary>
    /// The edit a validated <paramref name="content"/> asks for. A part the caller named keeps its id; any other part gets
    /// one from <paramref name="mint"/>. A yield's fractions are put in lowest terms.
    /// </summary>
    public static RecipeEdit From(RecipeContent content, Func<Guid> mint) => new(
        content.Title!,
        content.Summary,
        content.Yield is { } yield ? YieldOf(yield) : null,
        content.YieldUnit,
        content.PrepMinutes,
        content.CookMinutes,
        content.AdditionalMinutes,
        content.AdditionalTimeLabel,
        content.VideoUrl,
        [.. (content.IngredientGroups ?? []).Select(g => new RecipeEditGroup(
            g!.Id ?? mint(),
            g.Id is not null,
            g.Heading,
            [.. (g.Lines ?? []).Select(l => new RecipeEditPart(l!.Id ?? mint(), l.Id is not null, l.OriginalText!))]))],
        [.. (content.StepGroups ?? []).Select(g => new RecipeEditGroup(
            g!.Id ?? mint(),
            g.Id is not null,
            g.Heading,
            [.. (g.Steps ?? []).Select(s => new RecipeEditPart(s!.Id ?? mint(), s.Id is not null, s.Text!))]))],
        Texts(content.Equipment),
        Texts(content.Notes),
        Texts(content.Keywords),
        Texts(content.Courses),
        Texts(content.Cuisines),
        Texts(content.Diets),
        Texts(content.Allergens));

    /// <summary>
    /// A copy of <paramref name="source"/> as a new recipe titled <paramref name="title"/>: the same words, every part under
    /// an id from <paramref name="mint"/>, nothing claimed.
    /// </summary>
    public static RecipeEdit Copy(RecipeWorkingCopy source, string title, Func<Guid> mint)
    {
        var snapshot = source.ToSnapshot();
        return new(
            title,
            snapshot.Summary,
            snapshot.Yield,
            snapshot.YieldUnit,
            snapshot.PrepMinutes,
            snapshot.CookMinutes,
            snapshot.AdditionalMinutes,
            snapshot.AdditionalTimeLabel,
            snapshot.VideoUrl,
            [.. snapshot.IngredientGroups.Select(g => new RecipeEditGroup(
                mint(),
                false,
                g.Heading,
                [.. g.Lines.Select(l => new RecipeEditPart(mint(), false, l.OriginalText))]))],
            [.. snapshot.StepGroups.Select(g => new RecipeEditGroup(
                mint(),
                false,
                g.Heading,
                [.. g.Steps.Select(s => new RecipeEditPart(mint(), false, s.Text))]))],
            snapshot.Equipment,
            snapshot.Notes,
            snapshot.Keywords,
            snapshot.Courses,
            snapshot.Cuisines,
            snapshot.Diets,
            snapshot.Allergens);
    }

    private static Quantity YieldOf(RecipeQuantity yield)
    {
        var low = Rational.Create(yield.Low!.Numerator, yield.Low.Denominator);
        return yield.Kind == QuantityKind.Range
            ? Quantity.Range(low, Rational.Create(yield.High!.Numerator, yield.High.Denominator))
            : Quantity.Amount(low);
    }

    private static string[] Texts(IReadOnlyList<string?>? texts) => [.. (texts ?? []).Select(t => t!)];
}

/// <summary>
/// The rows one save changes, and the recipe as it will be (PP4.11). Only rows whose stored values change are updated, so
/// an autosave of one edited line writes that line and the recipe's row. The recipe's own row is the caller's to stage: it
/// is added for a new recipe and always updated for an existing one, which moves its version.
/// </summary>
internal sealed record RecipeEditPlan(
    RecipeWorkingCopy After,
    IReadOnlyList<object> Added,
    IReadOnlyList<object> Updated,
    IReadOnlyList<object> Removed);

/// <summary>
/// The rules of saving a recipe's working copy (RCP-001, RCP-002, RCP-007; PP4.11): which parts a save may name, who may
/// change a recipe, what a duplicate is called, and which rows a save adds, changes, and removes. Pure: it reads and
/// writes nothing, and mutates only the rows it is handed.
/// </summary>
internal static class RecipeEditor
{
    private const string CopyPrefix = "Copy of ";

    /// <summary>
    /// Whether <paramref name="role"/> lets <paramref name="caller"/> change, archive, or restore a recipe made by
    /// <paramref name="createdBy"/> (SITE-010): an Owner or Editor any recipe, an Author only their own. A recipe no person
    /// made (the publishing API, an import), or whose maker's account is gone, is no Author's.
    /// </summary>
    public static bool CanChange(SiteRole role, AccountId caller, AccountId? createdBy) =>
        role is SiteRole.Owner or SiteRole.Editor || (role == SiteRole.Author && createdBy == caller);

    /// <summary>A duplicate's title: "Copy of" before the original's, or the original's alone where that would not fit.</summary>
    public static string CopyTitle(string title) =>
        CopyPrefix.Length + title.Length <= RecipeLimits.MaxTitleLength ? CopyPrefix + title : title;

    /// <summary>
    /// The field errors for every part <paramref name="edit"/> names by an id that is not one of
    /// <paramref name="current"/>'s parts of that kind - unknown, another recipe's, or another site's: one message, so an id
    /// says nothing about anything the caller cannot read. Keyed by the JSON path of the id under
    /// <paramref name="prefix"/>; empty when every named part is the recipe's.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> UnknownParts(RecipeWorkingCopy current, RecipeEdit edit, string prefix)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Check(edit.IngredientGroups, "ingredientGroups", "lines", [.. current.IngredientGroups.Select(g => g.Id.Value)], [.. current.Ingredients.Select(i => i.Id.Value)]);
        Check(edit.StepGroups, "stepGroups", "steps", [.. current.StepGroups.Select(g => g.Id.Value)], [.. current.Steps.Select(s => s.Id.Value)]);
        return errors;

        void Check(IReadOnlyList<RecipeEditGroup> groups, string groupsName, string partsName, HashSet<Guid> groupIds, HashSet<Guid> partIds)
        {
            for (var g = 0; g < groups.Count; g++)
            {
                if (groups[g].Claimed && !groupIds.Contains(groups[g].Id))
                {
                    errors[$"{prefix}{groupsName}[{g}].id"] = [RecipeErrors.PartNotFoundMessage];
                }

                for (var p = 0; p < groups[g].Parts.Count; p++)
                {
                    if (groups[g].Parts[p].Claimed && !partIds.Contains(groups[g].Parts[p].Id))
                    {
                        errors[$"{prefix}{groupsName}[{g}].{partsName}[{p}].id"] = [RecipeErrors.PartNotFoundMessage];
                    }
                }
            }
        }
    }

    /// <summary>
    /// The field errors for every part <paramref name="edit"/> names by id when it creates a recipe: a new recipe has no
    /// parts, so every named id is one that is not its own, answered as <see cref="UnknownParts"/> answers a save.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> ClaimedParts(RecipeEdit edit) =>
        UnknownParts(RecipeWorkingCopy.New(new Recipe(), NoNutrition), edit, string.Empty);

    private static readonly RecipeSnapshotNutrition NoNutrition = new(null, null, null, null, null, null, null, null, null, null, null, null);

    /// <summary>
    /// Applies <paramref name="edit"/> to <paramref name="current"/>: sets the recipe's fields and marks it edited at
    /// <paramref name="now"/>, reads every ingredient line again with the parser (PP4.5) - nothing parsed is ever taken from
    /// a caller - and numbers every part from 0 in the order sent. A part keeps its row when its id is the recipe's, a line or
    /// step moving to another group of the same recipe included; any other part is a new row; a part not named is removed.
    /// Every part named by id must be <paramref name="current"/>'s (<see cref="UnknownParts"/> first).
    /// <para>
    /// It mutates the rows of <paramref name="current"/> in place. They must be untracked - read <c>AsNoTracking</c> under the
    /// recipe's lock - so that a save that turns out to change nothing, and is never staged, writes nothing; the unit of
    /// work clears the tracker after an attempt that saved nothing.
    /// </para>
    /// </summary>
    public static RecipeEditPlan Apply(RecipeWorkingCopy current, RecipeEdit edit, RecipeSnapshotNutrition nutrition, DateTimeOffset now)
    {
        var recipe = current.Recipe;
        recipe.Title = edit.Title;
        recipe.Summary = edit.Summary;
        recipe.Yield = edit.Yield;
        recipe.YieldUnit = edit.YieldUnit;
        recipe.PrepMinutes = edit.PrepMinutes;
        recipe.CookMinutes = edit.CookMinutes;
        recipe.AdditionalMinutes = edit.AdditionalMinutes;
        recipe.AdditionalTimeLabel = edit.AdditionalTimeLabel;
        recipe.VideoUrl = edit.VideoUrl;
        recipe.UpdatedAt = now;

        var added = new List<object>();
        var updated = new List<object>();
        var removed = new List<object>();

        var (ingredientGroups, ingredients) = Groups(
            current.IngredientGroups,
            current.Ingredients,
            edit.IngredientGroups,
            g => g.Id.Value,
            l => l.Id.Value,
            id => new RecipeIngredientGroup { Id = new RecipeIngredientGroupId(id), RecipeId = recipe.Id },
            id => new RecipeIngredient { Id = new RecipeIngredientId(id) },
            (group, position, heading) => Change(group.Position != position || group.Heading != heading, () => (group.Position, group.Heading) = (position, heading)),
            (line, group, position, text) => SetLine(line, group, position, text),
            added,
            updated,
            removed);

        var (stepGroups, steps) = Groups(
            current.StepGroups,
            current.Steps,
            edit.StepGroups,
            g => g.Id.Value,
            s => s.Id.Value,
            id => new RecipeStepGroup { Id = new RecipeStepGroupId(id), RecipeId = recipe.Id },
            id => new RecipeStep { Id = new RecipeStepId(id) },
            (group, position, heading) => Change(group.Position != position || group.Heading != heading, () => (group.Position, group.Heading) = (position, heading)),
            (step, group, position, text) => Change(
                step.GroupId.Value != group || step.Position != position || !string.Equals(step.Text, text, StringComparison.Ordinal),
                () => (step.GroupId, step.Position, step.Text) = (new RecipeStepGroupId(group), position, text)),
            added,
            updated,
            removed);

        return new RecipeEditPlan(
            new RecipeWorkingCopy(
                recipe,
                nutrition,
                ingredientGroups,
                ingredients,
                stepGroups,
                steps,
                Listed(current.Equipment, edit.Equipment, e => e.Text, (e, t) => e.Text = t, (p, t) => new RecipeEquipment { RecipeId = recipe.Id, Position = p, Text = t }, added, updated, removed),
                Listed(current.Notes, edit.Notes, n => n.Text, (n, t) => n.Text = t, (p, t) => new RecipeNote { RecipeId = recipe.Id, Position = p, Text = t }, added, updated, removed),
                Listed(current.Keywords, edit.Keywords, k => k.Text, (k, t) => k.Text = t, (p, t) => new RecipeKeyword { RecipeId = recipe.Id, Position = p, Text = t }, added, updated, removed),
                Chosen(current.Courses, edit.Courses, c => c.CourseCode, (c, p) => c.Position = p, (p, code) => new RecipeCourse { RecipeId = recipe.Id, CourseCode = code, Position = p }, added, updated, removed),
                Chosen(current.Cuisines, edit.Cuisines, c => c.CuisineCode, (c, p) => c.Position = p, (p, code) => new RecipeCuisine { RecipeId = recipe.Id, CuisineCode = code, Position = p }, added, updated, removed),
                Chosen(current.Diets, edit.Diets, d => d.DietCode, (d, p) => d.Position = p, (p, code) => new RecipeDiet { RecipeId = recipe.Id, DietCode = code, Position = p }, added, updated, removed),
                Chosen(current.Allergens, edit.Allergens, a => a.AllergenCode, (a, p) => a.Position = p, (p, code) => new RecipeAllergen { RecipeId = recipe.Id, AllergenCode = code, Position = p }, added, updated, removed)),
            added,
            updated,
            removed);
    }

    // A line's stored parts are the parser's reading of its text, never a caller's. Compared as stored, so a line whose text
    // is unchanged is rewritten only if the parser now reads it differently.
    private static bool SetLine(RecipeIngredient line, Guid group, int position, string text)
    {
        var parse = IngredientLineParser.Parse(text);
        var groupId = new RecipeIngredientGroupId(group);
        var parsed = parse.Status == IngredientParseStatus.Parsed;
        var quantity = parsed ? parse.Quantity : null;
        var quantitySpan = parsed ? parse.QuantitySpan : null;
        var unitSpan = parsed ? parse.UnitSpan : null;
        var unit = parsed ? parse.UnitCode : null;
        var item = parsed ? parse.Item : null;
        var note = parsed ? parse.Note : null;

        var same = line.GroupId == groupId
            && line.Position == position
            && string.Equals(line.OriginalText, text, StringComparison.Ordinal)
            && line.ParseStatus == parse.Status
            && line.Quantity == quantity
            && line.QuantityStart == quantitySpan?.Start && line.QuantityLength == quantitySpan?.Length
            && string.Equals(line.UnitCode, unit, StringComparison.Ordinal)
            && line.UnitStart == unitSpan?.Start && line.UnitLength == unitSpan?.Length
            && string.Equals(line.Item, item, StringComparison.Ordinal)
            && string.Equals(line.Note, note, StringComparison.Ordinal);
        if (same)
        {
            return false;
        }

        line.GroupId = groupId;
        line.Position = position;
        line.OriginalText = text;
        line.ParseStatus = parse.Status;
        line.Quantity = quantity;
        (line.QuantityStart, line.QuantityLength) = (quantitySpan?.Start, quantitySpan?.Length);
        line.UnitCode = unit;
        (line.UnitStart, line.UnitLength) = (unitSpan?.Start, unitSpan?.Length);
        line.Item = item;
        line.Note = note;
        return true;
    }

    private static bool Change(bool changes, Action apply)
    {
        if (changes)
        {
            apply();
        }

        return changes;
    }

    // Groups and their parts by id. A kept row is updated only when one of its values changes; a new one is added; one not
    // named is removed, parts before their group, so a group's removal never cascades over a part that moved out of it.
    private static (List<TGroup> Groups, List<TPart> Parts) Groups<TGroup, TPart>(
        IReadOnlyList<TGroup> currentGroups,
        IReadOnlyList<TPart> currentParts,
        IReadOnlyList<RecipeEditGroup> edited,
        Func<TGroup, Guid> groupId,
        Func<TPart, Guid> partId,
        Func<Guid, TGroup> newGroup,
        Func<Guid, TPart> newPart,
        Func<TGroup, int, string?, bool> setGroup,
        Func<TPart, Guid, int, string, bool> setPart,
        List<object> added,
        List<object> updated,
        List<object> removed)
        where TGroup : class
        where TPart : class
    {
        var groupsById = currentGroups.ToDictionary(groupId);
        var partsById = currentParts.ToDictionary(partId);
        var groups = new List<TGroup>();
        var parts = new List<TPart>();

        for (var g = 0; g < edited.Count; g++)
        {
            var editGroup = edited[g];
            if (groupsById.Remove(editGroup.Id, out var group))
            {
                if (setGroup(group, g, editGroup.Heading))
                {
                    updated.Add(group);
                }
            }
            else
            {
                group = newGroup(editGroup.Id);
                setGroup(group, g, editGroup.Heading);
                added.Add(group);
            }

            groups.Add(group);
            for (var p = 0; p < editGroup.Parts.Count; p++)
            {
                var editPart = editGroup.Parts[p];
                if (partsById.Remove(editPart.Id, out var part))
                {
                    if (setPart(part, editGroup.Id, p, editPart.Text))
                    {
                        updated.Add(part);
                    }
                }
                else
                {
                    part = newPart(editPart.Id);
                    setPart(part, editGroup.Id, p, editPart.Text);
                    added.Add(part);
                }

                parts.Add(part);
            }
        }

        removed.AddRange(partsById.Values);
        removed.AddRange(groupsById.Values);
        return (groups, parts);
    }

    // A list keyed by position (equipment, notes, keywords): a position kept is rewritten only if its text changes.
    private static List<T> Listed<T>(
        IReadOnlyList<T> current,
        IReadOnlyList<string> edited,
        Func<T, string> text,
        Action<T, string> setText,
        Func<int, string, T> create,
        List<object> added,
        List<object> updated,
        List<object> removed)
        where T : class
    {
        var byPosition = current.ToDictionary(e => PositionOf(e));
        var result = new List<T>();
        for (var p = 0; p < edited.Count; p++)
        {
            if (byPosition.Remove(p, out var kept))
            {
                if (!string.Equals(text(kept), edited[p], StringComparison.Ordinal))
                {
                    setText(kept, edited[p]);
                    updated.Add(kept);
                }

                result.Add(kept);
            }
            else
            {
                var item = create(p, edited[p]);
                added.Add(item);
                result.Add(item);
            }
        }

        removed.AddRange(byPosition.Values);
        return result;
    }

    // A set of vocabulary codes: a code kept is rewritten only if its position changes.
    private static List<T> Chosen<T>(
        IReadOnlyList<T> current,
        IReadOnlyList<string> edited,
        Func<T, string> code,
        Action<T, int> setPosition,
        Func<int, string, T> create,
        List<object> added,
        List<object> updated,
        List<object> removed)
        where T : class
    {
        var byCode = current.ToDictionary(code, StringComparer.Ordinal);
        var result = new List<T>();
        for (var p = 0; p < edited.Count; p++)
        {
            if (byCode.Remove(edited[p], out var kept))
            {
                if (PositionOf(kept) != p)
                {
                    setPosition(kept, p);
                    updated.Add(kept);
                }

                result.Add(kept);
            }
            else
            {
                var item = create(p, edited[p]);
                added.Add(item);
                result.Add(item);
            }
        }

        removed.AddRange(byCode.Values);
        return result;
    }

    private static int PositionOf(object part) => part switch
    {
        RecipeEquipment e => e.Position,
        RecipeNote n => n.Position,
        RecipeKeyword k => k.Position,
        RecipeCourse c => c.Position,
        RecipeCuisine c => c.Position,
        RecipeDiet d => d.Position,
        RecipeAllergen a => a.Position,
        _ => throw new InvalidOperationException($"{part.GetType().Name} is not a listed part."),
    };
}
