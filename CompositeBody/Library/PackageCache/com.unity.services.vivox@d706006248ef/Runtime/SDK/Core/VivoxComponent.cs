using Unity.Services.Vivox.Internal;

namespace Unity.Services.Vivox
{
    class VivoxComponent : IVivox
    {
        readonly VivoxServiceInternal m_vivoxService;

        internal VivoxComponent(VivoxServiceInternal vivoxService)
        {
            m_vivoxService = vivoxService;
        }

        public void RegisterTokenProvider(IVivoxTokenProviderInternal tokenProvider)
        {
            m_vivoxService.InternalTokenProvider = tokenProvider;
        }
    }
}
