using System.ComponentModel.DataAnnotations;

namespace Acme.WasmContracts;

/// <summary>
/// The status of an order, labelled for display. It lives in a plain class library — no Razor content — so it
/// exercises the path a contracts or model project takes: annotations only, extracted at build, and rendered by
/// a WebAssembly client that must be able to fetch this library's catalogs.
/// </summary>
public enum OrderStatus
{
    /// <summary>The order is awaiting payment.</summary>
    [Display(Name = "Awaiting payment")]
    AwaitingPayment,

    /// <summary>The order has shipped.</summary>
    [Display(Name = "On its way")]
    Shipped,
}
