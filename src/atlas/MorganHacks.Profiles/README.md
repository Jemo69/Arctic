# MorganHacks.Profiles

Referenced by `MorganHacks.Api`'s project file and otherwise empty.
`IProfiles` declares no members, there is no `profiles.*` schema in any
migration script, and nothing in the codebase is registered against this
namespace.

What this was meant to hold — resume metadata, dietary and accessibility
needs — was built inside `MorganHacks.Applications` instead: see
`Domain/ApplicantProfile.cs` and `Storage/AzureResumeStore.cs`. If a Profiles
module gets carved out for real later, that is the code it comes from.
