namespace Primitives.TypedIdsSample.Catalog;

// Phantom marker — never instantiated, only tags Id<UserTag> at compile time
// so a user id can never be assigned where an order id is expected.
internal sealed class UserTag;
