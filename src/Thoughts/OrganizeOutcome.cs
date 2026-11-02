namespace Thoughts;

/// <summary>What the AI did for one Thought, for the capture strip. Null fields mean it had nothing worth saying.</summary>
sealed record OrganizeOutcome(string Title, string? FiledIn, string? Person, string? SuggestedProject, string? NewProject, string? Why, string Was, ThoughtRelation? Relation = null);

/// <summary>How a new Thought relates to an earlier one, suggested by the AI. Nothing is written until the person chooses.</summary>
sealed record ThoughtRelation(bool MoreDetail, string Target, string Evidence, string Why);
