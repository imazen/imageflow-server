// namespace Imazen.Abstractions.Blobs.Drafts
// {
//     internal interface IBlobRequestOptions
//     {
//         /// <summary>
//         /// Cache layers need to store these tags to be queried later, such as for
//         /// redaction, invalidation, or compliance/auditing.
//         /// There also may be tags in the source blob. so maybe this needs to be mutable?
//         /// </summary>
//         IList<string> ApplyCacheTags { get; }
//
//         bool FetchContentType { get; }
//         
//         bool FetchMetadata { get; }
//         
//         bool RequireReusable { get; }
//         
//     }
// }