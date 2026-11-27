namespace NucleusVR.Secrets
{
    public sealed class FacilitySecrets
    {
        public const uint GoldenCore = 1u << 0;
        public const uint Room404 = 1u << 1;
        public const uint TinyCreature = 1u << 2;
        public const uint SecretRadio = 1u << 3;
        public const uint DeveloperRoom = 1u << 4;
        public const uint HiddenBoss = 1u << 5;
        public const uint StrangeComputer = 1u << 6;
        public const uint ForgottenExperiment = 1u << 7;
        public const uint KnownMask = (1u << 8) - 1;

        private const uint AllFragments = 0b111;
        private static readonly ushort[] CircuitOrder = { 105, 104, 106 };

        private uint fragments;
        private uint circuitStep;
        private uint radioStep;
        private uint terminalStep;
        private bool readTranscript;
        private bool bossDefeated;

        public uint Progress { get; private set; }
        public uint LastReward { get; private set; }

        // Shared with facility presentation and the replicated run snapshot.
        public uint PuzzleBits => fragments | (circuitStep << 3) | (radioStep << 6) |
            (bossDefeated ? 1u << 8 : 0u) | (terminalStep << 9) | (readTranscript ? 1u << 11 : 0u);

        // A recognized station may also return an unmet prerequisite as feedback.
        // LastReward identifies a valid personal claim by the actor for this interaction.
        public bool Use(ushort site, out string feedback)
        {
            LastReward = 0;
            switch (site)
            {
                case 101:
                case 102:
                case 103:
                    feedback = CollectFragment(site);
                    return true;
                case 104:
                case 105:
                case 106:
                    feedback = AlignCircuit(site);
                    return true;
                case 107:
                    feedback = circuitStep == CircuitOrder.Length
                        ? Discover(GoldenCore, "Golden Core recovered. Its containment field still pulses.")
                        : "The vault needs three Core fragments and a stable circuit.";
                    return true;
                case 110:
                    feedback = Has(SecretRadio)
                        ? Discover(Room404, "Room 404 logged. The archive transcript is still readable.")
                        : "A signal lock protects this door. Tune the facility radio.";
                    return true;
                case 111:
                    feedback = ReadTranscript();
                    return true;
                case 112:
                    feedback = Discover(TinyCreature, "You spotted a tiny creature. It hides a tiny staff badge.");
                    return true;
                case 113:
                    feedback = TuneRadio();
                    return true;
                case 114:
                    feedback = Has(StrangeComputer)
                        ? Discover(DeveloperRoom, "Developer Room logged. An old staff pass fits the containment controls.")
                        : "Restore the facility terminal archive to authorize this room.";
                    return true;
                case 115:
                    feedback = ArmBossDoor();
                    return true;
                case 116:
                case 117:
                case 118:
                    feedback = RunTerminalCommand(site);
                    return true;
                case 119:
                    feedback = Discover(ForgottenExperiment, "Specimen record recovered. The researchers reported it copying their knocks.");
                    return true;
                default:
                    feedback = string.Empty;
                    return false;
            }
        }

        public bool CompleteBoss()
        {
            LastReward = 0;
            if (!Has(HiddenBoss) || bossDefeated)
                return false;

            bossDefeated = true;
            return true;
        }

        private string CollectFragment(ushort site)
        {
            uint fragment = 1u << (site - 101);
            if ((fragments & fragment) != 0)
                return "This Core fragment is already in the circuit.";

            fragments |= fragment;
            return fragments == AllFragments
                ? "All three Core fragments connected. Restore the circuit in amber, blue, green order."
                : "Core fragment connected. Follow the marked cables to find the others.";
        }

        private string AlignCircuit(ushort site)
        {
            if (fragments != AllFragments)
                return "The circuit needs all three Core fragments.";
            if (circuitStep == CircuitOrder.Length)
                return "The circuit is stable. The Golden Core vault is open.";

            if (CircuitOrder[circuitStep] != site)
            {
                circuitStep = site == CircuitOrder[0] ? 1u : 0u;
                return circuitStep == 1
                    ? "The circuit restarted on amber. Continue with blue, then green."
                    : "The relay tripped. Start again with amber, then blue, then green.";
            }

            circuitStep++;
            return circuitStep == CircuitOrder.Length
                ? "The circuit is stable. The Golden Core vault is open."
                : "The relay held. Find the next switch in the circuit sequence.";
        }

        private string TuneRadio()
        {
            if (radioStep < 3)
                radioStep++;

            return radioStep switch
            {
                1 => "A recording breaks through the static. Containment personnel were told to keep Room 404 off the maps.",
                2 => "A second recording mentions a door in Containment that opens to a repeating radio tone.",
                _ => Discover(SecretRadio, "The radio tone unlocked Room 404. Search the southeast side of Containment.")
            };
        }

        private string ReadTranscript()
        {
            if (!Has(Room404))
                return "Log your Room 404 discovery at its entry station first.";

            readTranscript = true;
            return "Room 404 transcript recovered. Bring a developer staff pass to the hidden containment release.";
        }

        private string ArmBossDoor()
        {
            if (bossDefeated)
                return Discover(HiddenBoss, "Hidden experiment defeated. The containment record is yours.");
            if (Has(HiddenBoss))
                return "The hidden chamber is open. Return here to log the encounter after it ends.";
            if (!readTranscript || !Has(DeveloperRoom))
                return "The release needs the Room 404 transcript and a Developer Room staff pass.";

            Progress |= HiddenBoss;
            return "The hidden chamber is open. The containment experiment is awake.";
        }

        private string RunTerminalCommand(ushort site)
        {
            switch (site)
            {
                case 116:
                    if (terminalStep == 0)
                        terminalStep = 1;
                    return "Facility terminal commands are HELP, STATUS and ARCHIVE. Read STATUS to connect to the archive.";
                case 117:
                    if (terminalStep == 0)
                        return "Run HELP to initialize the terminal.";
                    if (terminalStep == 1)
                        terminalStep = 2;
                    return "Archive connection ready. A staff access record lists a retired developer room.";
                default:
                    if (terminalStep < 2)
                        return "Read HELP and STATUS before opening the archive.";
                    terminalStep = 3;
                    return Discover(StrangeComputer, "Archive opened. Developer Room access is authorized.");
            }
        }

        private bool Has(uint discovery) => (Progress & discovery) != 0;

        private string Discover(uint discovery, string feedback)
        {
            Progress |= discovery;
            LastReward = discovery;
            return feedback;
        }
    }
}
