#!/usr/bin/env bash
# Prepara la demo completa en local: Odoo 19 con Ventas e Inventario, WordPress con WooCommerce, los
# mismos productos en los dos (unidos por el SKU), las claves de API, los webhooks y el sincronizador.
# Uso (desde la raíz del repositorio):  ./demo/preparar.sh
# Volver a empezar de cero:             docker compose down -v && ./demo/preparar.sh
set -euo pipefail
# Git Bash en Windows convierte las rutas «/var/...» en rutas de Windows: aquí no debe
export MSYS_NO_PATHCONV=1
cd "$(dirname "$0")/.."

azar() { head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n'; }
wp() { docker compose run --rm -T wpcli wp "$@"; }

echo "» Arrancando bases de datos, Odoo y WordPress…"
docker compose up -d sincro-db odoo-db odoo woo-db wordpress

echo "» Odoo: base de datos «tienda» con Ventas e Inventario (la primera vez tarda un par de minutos)…"
if ! docker compose exec -T odoo-db psql -U odoo -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='tienda'" | grep -q 1; then
  docker compose stop odoo
  docker compose run --rm -T odoo odoo -d tienda -i sale_management,stock --without-demo=all --stop-after-init --no-http
  docker compose start odoo
fi
CLAVE_ODOO=$(docker compose run --rm -T odoo odoo shell -d tienda --no-http < demo/odoo_preparar.py 2>/dev/null | grep '^CLAVE_ODOO=' | cut -d= -f2)
[ -n "$CLAVE_ODOO" ] || { echo "No se pudo crear la clave de API de Odoo"; exit 1; }

echo "» WordPress y WooCommerce…"
until docker compose exec -T wordpress test -f /var/www/html/wp-config.php; do sleep 2; done
if ! wp core is-installed 2>/dev/null; then
  wp core install --url=http://localhost:8080 --title="Tienda demo" --admin_user=admin --admin_password=admin-demo \
    --admin_email=admin@ejemplo.es --skip-email
fi
wp plugin is-active woocommerce 2>/dev/null || wp plugin install woocommerce --activate
wp rewrite structure '/%postname%/' >/dev/null
wp option update woocommerce_currency EUR >/dev/null
wp option update woocommerce_default_country ES:MU >/dev/null
wp option update woocommerce_prices_include_tax no >/dev/null

crear_producto() { # sku nombre precio stock
  if [ -z "$(wp wc product list --user=admin --sku="$1" --field=id)" ]; then
    wp wc product create --user=admin --name="$2" --sku="$1" --regular_price="$3" --manage_stock=true \
      --stock_quantity="$4" --status=publish --porcelain >/dev/null
  fi
}
crear_producto CAF-250 "Café molido 250 g" 3.50 40
crear_producto CAF-1K "Café en grano 1 kg" 14.90 15
crear_producto TAZA-01 "Taza de cerámica" 6.00 25
crear_producto FILT-100 "Filtros de papel (100 uds.)" 2.20 60

# Clave de la API REST para el sincronizador (WP-CLI no tiene un comando para crearla)
read -r CLAVE_WOO SECRETO_WOO < <(wp eval '
  global $wpdb;
  $ck = "ck_" . wc_rand_hash(); $cs = "cs_" . wc_rand_hash();
  $wpdb->insert( $wpdb->prefix . "woocommerce_api_keys", array(
    "user_id" => 1, "description" => "sincro-tienda (demo)", "permissions" => "read_write",
    "consumer_key" => wc_api_hash( $ck ), "consumer_secret" => $cs, "truncated_key" => substr( $ck, -7 ) ) );
  echo $ck . " " . $cs . PHP_EOL;')

SECRETO_WEBHOOK=$(azar)
for id in $(wp wc webhook list --user=admin --field=id); do wp wc webhook delete "$id" --user=admin --force=true >/dev/null; done
for tema in order.created order.updated; do
  wp wc webhook create --user=admin --name="Sincro: $tema" --topic="$tema" --status=active \
    --delivery_url=http://sincro:8080/webhooks/woocommerce --secret="$SECRETO_WEBHOOK" --porcelain >/dev/null
done

CLAVE_ADMIN=$(azar)
cat > demo/.env <<FIN
WooCommerce__ClaveCliente=$CLAVE_WOO
WooCommerce__SecretoCliente=$SECRETO_WOO
WooCommerce__SecretoWebhook=$SECRETO_WEBHOOK
Odoo__ClaveApi=$CLAVE_ODOO
Administracion__ClaveApi=$CLAVE_ADMIN
FIN

echo "» Sincronizador…"
docker compose up -d --build sincro
until curl -fs http://localhost:5080/health >/dev/null; do sleep 2; done

cat <<FIN

Listo.
  Tienda (WooCommerce):  http://localhost:8080/wp-admin   admin / admin-demo
  ERP (Odoo):            http://localhost:8069            admin / admin
  Sincronizador:         http://localhost:5080/openapi/v1.json
  Clave de administración (cabecera X-Api-Key): $CLAVE_ADMIN   (está en demo/.env)

Prueba un pedido:  ./demo/pedido.sh
FIN
