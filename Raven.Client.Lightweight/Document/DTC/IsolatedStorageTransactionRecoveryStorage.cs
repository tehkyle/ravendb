#if !DNXCORE50
namespace Raven.Client.Document.DTC
{
    public class IsolatedStorageTransactionRecoveryStorage : ITransactionRecoveryStorage
    {
        public ITransactionRecoveryStorageContext Create()
        {
            return new IsolatedStorageTransactionRecoveryContext();
        }
    }
}
#endif