using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobTracker.Api;

/// <summary>
/// Reads ANY JSON token into text instead of throwing, so a malformed <c>id</c> reaches the application's validator rather
/// than dying in the deserializer. <c>BEHAVIOR-057</c> / AC-8.
///
/// ## Why <c>string?</c> alone was not enough
///
/// Widening <c>Guid Id</c> to <c>string? Id</c> fixed the string-shaped mistakes — <c>"not-a-guid"</c>, <c>""</c>, a
/// truncated paste — because those were always strings and the binder had simply refused to convert them. It did nothing for
/// <c>{"id":123}</c> or <c>{"id":{"kind":"uuid"}}</c>: System.Text.Json will not bind a number or an object to a string
/// either, so those two kept answering 500 in a run that otherwise looked green. **The class of the bug is "the framework
/// parses before the app can", and fixing the type of one field is only fixing it for inputs that happen to already be that
/// type.**
///
/// ## What this is NOT
///
/// It is not a global leniency policy, and it is deliberately attached to one member. Applying it everywhere would accept
/// <c>{"companyName":42}</c> and store "42", which is a different product — the honest answer for a wrong-typed
/// <c>companyName</c> is still a 4xx this app controls, and that is a separate behaviour row (see the task record's note on
/// the sibling shapes measured today). Here, the value in question is a machine identifier, and the only decision made about
/// it is "does it parse as a GUID", so rendering it as text loses nothing that the validator would not have rejected anyway.
///
/// Absent or <c>null</c> become <c>null</c>, which the validator reports as "An id is required." — the same answer the other
/// members give for a missing value.
/// </summary>
public sealed class AnyTokenToTextConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            // ParseValue advances the reader past the whole value, which is what makes objects and arrays safe to render
            // here; GetRawText() alone would only cover the first token of a composite.
            _ => JsonDocument.ParseValue(ref reader).RootElement.GetRawText()
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value ?? string.Empty);
}
