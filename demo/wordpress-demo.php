<?php
/**
 * Plugin Name: Ajustes de la demo de sincro-tienda
 * Description: Solo para el entorno local de Docker. No usar en una tienda real.
 */

// WordPress no entrega webhooks a direcciones privadas (protección SSRF) y el sincronizador está en
// la red interna de Docker. Se permiten solo los nombres de los servicios de la demo.
add_filter( 'http_request_host_is_external', function ( $externo, $host ) {
	return $externo || in_array( $host, array( 'sincro' ), true );
}, 10, 2 );

// Entregar los webhooks al momento en lugar de en segundo plano: sin visitas, el cron de WordPress
// tardaría en dispararse y la demo se vería lenta.
add_filter( 'woocommerce_webhook_deliver_async', '__return_false' );

// Sin servidor de correo en la demo: no se intenta enviar (evita los errores de sendmail).
add_filter( 'pre_wp_mail', '__return_false' );
