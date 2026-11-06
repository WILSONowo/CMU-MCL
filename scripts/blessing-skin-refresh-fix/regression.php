<?php
declare(strict_types=1);
// Executes the upstream UUID expressions and actual Token::isValid method with
// real Ramsey UUID and Lcobucci JWT dependencies. Uses fixture credentials only.
use Lcobucci\JWT\Configuration;
use Lcobucci\JWT\Signer\Hmac\Sha256;
use Lcobucci\JWT\Signer\Key\InMemory;
use Lcobucci\JWT\Validation\Constraint\SignedWith;
require $argv[1] ?? throw new RuntimeException('autoload.php path required');
require $argv[4] ?? throw new RuntimeException('upstream Token.php path required');
const FIXTURE_KEY = 'fixture-only-64-character-secret-for-local-regression-never-shipped';
function config(string $name, mixed $fallback = null): mixed { return $name === 'jwt.secret' ? FIXTURE_KEY : $fallback; }
function option(string $name): int { return 259200; }
function identity(string $controller, int $stage, string $email): string {
    $user = (object) ['email' => $email];
    preg_match_all('/\$userUuid\s*=\s*(Uuid::uuid5\(Uuid::NAMESPACE_DNS,.*?\)->getHex\(\)->toString\(\));/', file_get_contents($controller), $matches);
    if (count($matches[1]) !== 2) throw new RuntimeException('Unexpected controller identity expressions; manual review required');
    return eval('return '.str_replace('Uuid::', '\\Ramsey\\Uuid\\Uuid::', $matches[1][$stage]).';');
}
function tokenFor(string $owner, string $subject): Yggdrasil\Models\Token {
    $cfg = Configuration::forSymmetricSigner(new Sha256(), InMemory::plainText(FIXTURE_KEY));
    $now = new DateTimeImmutable();
    $jwt = $cfg->builder()->issuedBy('Yggdrasil-Auth')->relatedTo($subject)
        ->issuedAt($now)->expiresAt($now->modify('+3 days'))
        ->withClaim('yggt', Ramsey\Uuid\Uuid::uuid4()->getHex()->toString())
        ->withClaim('spr', str_repeat('a', 32))->getToken($cfg->signer(), $cfg->signingKey());
    if (!$cfg->validator()->validate($jwt, new SignedWith($cfg->signer(), $cfg->signingKey()))) throw new RuntimeException('Fixture JWT signature invalid');
    $model = new Yggdrasil\Models\Token('fixture-client', $jwt->toString());
    $model->owner = $owner; $model->profileId = str_repeat('a', 32);
    return $model;
}
$checks = 0;
function check(bool $ok, string $label): void {
    if (!$ok) throw new RuntimeException('FAIL '.$label);
    $GLOBALS['checks']++; echo 'PASS '.$label.PHP_EOL;
}
$original = $argv[2]; $patched = $argv[3];
foreach (['player@example.test', 'Player@example.test', 'player@Example.TEST', 'PLAYER@EXAMPLE.TEST'] as $i => $email) {
    $auth = identity($original, 0, $email);
    $before = identity($original, 1, $email);
    $after = identity($patched, 1, $email);
    check(tokenFor($email, $auth)->isValid(), "fixture $i: authenticate token accepted by unmodified upstream validator");
    check(tokenFor($email, $before)->isValid() === ($i === 0), "fixture $i: baseline refresh reproduces mixed-case identity rejection");
    check($after === $auth, "fixture $i: patched refresh preserves existing account identity");
    check(tokenFor($email, $after)->isValid(), "fixture $i: patched refresh accepted by unmodified upstream validator");
    check(identity($patched, 0, $email) === $auth, "fixture $i: patch preserves authenticate identity and existing tokens");
    for ($rotation = 0; $rotation < 3; $rotation++)
        check(tokenFor($email, identity($patched, 1, $email))->isValid(), "fixture $i: repeated refresh $rotation stays valid");
}
echo 'PASS '.$checks.' upstream identity/JWT regression checks'.PHP_EOL;
