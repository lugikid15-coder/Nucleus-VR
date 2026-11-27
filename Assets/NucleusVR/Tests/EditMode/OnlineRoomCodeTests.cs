using NucleusVR.Multiplayer;
using NUnit.Framework;

namespace NucleusVR.Tests
{
    public sealed class OnlineRoomCodeTests
    {
        [TestCase("q4T7p2", "Q4T7P2")]
        [TestCase("  zT32s8\n", "ZT32S8")]
        [TestCase("012345", "012345")]
        public void AcceptsSixAsciiCharactersAndNormalizesForLobbyJoin(string input, string expected)
        {
            Assert.That(OnlineRoomCode.TryNormalize(input, out string result), Is.True);
            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("12345")]
        [TestCase("1234567")]
        [TestCase("AB CD1")]
        [TestCase("AB-CD1")]
        [TestCase("ÅBC123")]
        [TestCase("ＡBC123")]
        public void RejectsMalformedRoomCodesBeforeAnyServiceRequest(string input)
        {
            Assert.That(OnlineRoomCode.TryNormalize(input, out string result), Is.False);
            Assert.That(result, Is.Empty);
        }
    }
}
