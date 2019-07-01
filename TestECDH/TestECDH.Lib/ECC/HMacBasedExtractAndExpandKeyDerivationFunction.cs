//using Windows.Security.Cryptography.Core;
//using Windows.Storage.Streams;

namespace EllipticCurveCrypto
{
    ///// <summary>
    ///// Implement HMAC-based Extract-and-Expand Key Derivation Function (HKDF) 
    ///// https://tools.ietf.org/html/rfc5869
    ///// </summary>
    //public class HMacBasedExtractAndExpandKeyDerivationFunction
    //{
    //    private MacAlgorithmProvider _macProv;
    //    public IBuffer DeriveKey(string algName, IBuffer salt, IBuffer info, IBuffer ikm)
    //    {
    //        _macProv = MacAlgorithmProvider.OpenAlgorithm(algName);

    //        IBuffer prk = HkdfExtract(_macProv, salt, ikm);

    //        return HkdfExpend(_macProv, prk, info);
    //    }

    //    /// <summary>
    //    /// 
    //    /// </summary>
    //    /// <param name="macProv"></param>
    //    /// <param name="salt"> Optional salt value (a non-secret random value) </param>
    //    /// <param name="ikm"> Input keying material </param>
    //    private IBuffer HkdfExtract(MacAlgorithmProvider macProv, IBuffer salt, IBuffer ikm)
    //    {
    //        return HMac(macProv, salt, ikm);
    //    }

    //    private IBuffer HkdfExpend(MacAlgorithmProvider macProv, IBuffer prk, IBuffer info)
    //    {
    //        return HMac(macProv, prk, info);
    //    }

    //    private IBuffer HMac(MacAlgorithmProvider macProv, IBuffer macKey, IBuffer keyMaterial)
    //    {
    //        var saltKey = macProv.CreateKey(macKey);
    //        IBuffer hMacValue = CryptographicEngine.Sign(saltKey, keyMaterial);

    //        return hMacValue;
    //    }

    //}
}