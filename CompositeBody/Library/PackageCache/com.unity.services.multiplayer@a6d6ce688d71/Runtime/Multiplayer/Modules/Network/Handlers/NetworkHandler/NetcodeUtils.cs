namespace Unity.Services.Multiplayer
{
    static class NetcodeUtils
    {
#if GAMEOBJECTS_NETCODE_AVAILABLE
        public static NetcodeType Current => NetcodeType.GameObjects;
#elif ENTITIES_NETCODE_AVAILABLE
        public static NetcodeType Current => NetcodeType.Entities;
#else
        public static NetcodeType Current => NetcodeType.Unknown;
#endif
    }
}
