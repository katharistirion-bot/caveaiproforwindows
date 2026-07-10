using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.UserProfile;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class UserProfileTests
{
    [TestMethod]
    public void IsCompleteForPublish_requires_trimmed_name_and_country()
    {
        var complete = new UserProfileDocument
        {
            Uid = "uid-1",
            FirstName = "Jane",
            LastName = " Doe ",
            Country = " Greece ",
        };
        Assert.IsTrue(complete.IsCompleteForPublish());

        var missingCountry = new UserProfileDocument
        {
            FirstName = "Jane",
            LastName = "Doe",
            Country = "   ",
        };
        Assert.IsFalse(missingCountry.IsCompleteForPublish());
    }

    [TestMethod]
    public void HasPublisherIdentity_checks_immutable_published_cave_fields()
    {
        Assert.IsFalse(CloudPublishProfileGate.HasPublisherIdentity(new PublishedCaveDocument()));

        Assert.IsTrue(CloudPublishProfileGate.HasPublisherIdentity(new PublishedCaveDocument
        {
            PublisherFirstName = "Ada",
            PublisherLastName = "Lovelace",
            PublisherCountry = "UK",
        }));
    }
}