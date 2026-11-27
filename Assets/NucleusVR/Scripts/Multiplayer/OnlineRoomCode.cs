namespace NucleusVR.Multiplayer
{
    public static class OnlineRoomCode
    {
        public const int Length = 6;

        public static bool TryNormalize(string input, out string code)
        {
            code = "";
            if (input == null)
                return false;

            string candidate = input.Trim();
            if (candidate.Length != Length)
                return false;

            foreach (char character in candidate)
            {
                if (!(character >= 'A' && character <= 'Z') &&
                    !(character >= 'a' && character <= 'z') &&
                    !(character >= '0' && character <= '9'))
                    return false;
            }

            code = candidate.ToUpperInvariant();
            return true;
        }
    }
}
