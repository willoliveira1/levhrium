using Levhrium.Core;

namespace Levhrium.Core.Tests;

/// <summary>
/// A politica de hibernacao. Testada com rigor porque hibernar a aba errada
/// trava o progresso de um jogo idle -- o cenario que o projeto existe para servir.
/// </summary>
public sealed class HibernationPolicyTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static TabHibernationInfo Aba(
        bool isActive = false,
        bool pinned = false,
        bool suspended = false,
        string url = "https://exemplo.com/",
        int ociosaHaMinutos = 10) => new()
        {
            IsActive = isActive,
            Pinned = pinned,
            IsSuspended = suspended,
            Url = url,
            LastActiveAt = Agora.AddMinutes(-ociosaHaMinutos),
        };

    [Fact]
    public void AbaDeFundoOciosaEHibernada()
    {
        var policy = new HibernationPolicy();

        Assert.Equal(HibernationAction.Suspend, policy.Decide(Aba(ociosaHaMinutos: 6), Agora));
    }

    [Fact]
    public void AbaDeFundoRecenteNaoEHibernada()
    {
        var policy = new HibernationPolicy();

        Assert.Equal(HibernationAction.None, policy.Decide(Aba(ociosaHaMinutos: 4), Agora));
    }

    [Fact]
    public void LimiteDeCincoMinutosEInclusivo()
    {
        var policy = new HibernationPolicy();

        Assert.Equal(HibernationAction.Suspend, policy.Decide(Aba(ociosaHaMinutos: 5), Agora));
    }

    [Fact]
    public void AbaAtivaNuncaHiberna()
    {
        var policy = new HibernationPolicy();

        Assert.Equal(HibernationAction.None, policy.Decide(Aba(isActive: true), Agora));
    }

    [Fact]
    public void AbaAtivaQueEstavaCongeladaEDescongelada()
    {
        var policy = new HibernationPolicy();

        Assert.Equal(
            HibernationAction.Resume,
            policy.Decide(Aba(isActive: true, suspended: true), Agora));
    }

    [Fact]
    public void AbaFixadaNuncaHiberna()
    {
        var policy = new HibernationPolicy();

        // O pin e a isencao permanente: foi a decisao tomada para o jogo idle.
        Assert.Equal(
            HibernationAction.None,
            policy.Decide(Aba(pinned: true, ociosaHaMinutos: 600), Agora));
    }

    [Fact]
    public void DominioProtegidoNuncaHiberna()
    {
        var policy = new HibernationPolicy();

        Assert.Equal(
            HibernationAction.None,
            policy.Decide(Aba(url: "https://poke.sigmaproject.online/play", ociosaHaMinutos: 600), Agora));
    }

    [Fact]
    public void SubdominioDeDominioProtegidoTambemEIsento()
    {
        var policy = new HibernationPolicy(neverHibernate: ["jogo.test"]);

        Assert.Equal(
            HibernationAction.None,
            policy.Decide(Aba(url: "https://mundo.jogo.test/play", ociosaHaMinutos: 600), Agora));

        // Mas um dominio que apenas termina igual nao conta.
        Assert.Equal(
            HibernationAction.Suspend,
            policy.Decide(Aba(url: "https://naojogo.test/", ociosaHaMinutos: 600), Agora));
    }

    [Fact]
    public void AbaJaCongeladaNaoECongeladaDeNovo()
    {
        var policy = new HibernationPolicy();

        Assert.Equal(
            HibernationAction.None,
            policy.Decide(Aba(suspended: true, ociosaHaMinutos: 600), Agora));
    }

    [Fact]
    public void LimiarZeroDesligaAHibernacao()
    {
        var policy = new HibernationPolicy(TimeSpan.Zero);

        Assert.False(policy.Enabled);
        Assert.Equal(HibernationAction.None, policy.Decide(Aba(ociosaHaMinutos: 600), Agora));
    }

    [Fact]
    public void LimiarZeroAindaDescongelaAAbaAtiva()
    {
        var policy = new HibernationPolicy(TimeSpan.Zero);

        // Desligar a hibernacao nunca pode deixar uma aba congelada para sempre.
        Assert.Equal(
            HibernationAction.Resume,
            policy.Decide(Aba(isActive: true, suspended: true), Agora));
    }

    [Fact]
    public void AbaDeDominioProtegidoNasceFixada()
    {
        var policy = new HibernationPolicy();

        Assert.True(policy.ShouldPinOnOpen("https://poke.sigmaproject.online/play"));
        Assert.False(policy.ShouldPinOnOpen("https://exemplo.com/"));
    }

    [Fact]
    public void UrlInvalidaNaoQuebraAPolitica()
    {
        var policy = new HibernationPolicy();

        Assert.False(policy.IsNeverHibernate("about:blank"));
        Assert.False(policy.IsNeverHibernate("nao e uma url"));
    }
}
